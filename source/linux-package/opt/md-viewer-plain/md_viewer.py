#!/usr/bin/python3
from __future__ import annotations

import hashlib
import json
import mimetypes
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("WebKit", "6.0")
gi.require_version("JavaScriptCore", "6.0")
from gi.repository import Gdk, Gio, GLib, Gtk, WebKit

from docx_export import export as export_docx


APP_ID = "io.github.mdviewer.Plain"
APP_DIR = Path(__file__).resolve().parent
WEB_ROOT = APP_DIR / "wwwroot"
MAX_BYTES = 25 * 1024 * 1024
IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".avif"}


@dataclass
class Document:
    id: str
    path: Path
    content: str
    mtime_ns: int
    status: str = "ready"

    def snapshot(self) -> dict:
        return {
            "id": self.id,
            "name": self.path.name,
            "content": self.content,
            "mtimeMs": self.mtime_ns / 1_000_000,
            "status": self.status,
        }


class MDViewer(Gtk.Application):
    def __init__(self) -> None:
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.HANDLES_OPEN)
        self.window: Gtk.ApplicationWindow | None = None
        self.webview: WebKit.WebView | None = None
        self.manager: WebKit.UserContentManager | None = None
        self.documents: dict[str, Document] = {}
        self.page_ready = False
        self.pending_open: list[dict] = []
        self._poll_id = 0

    def do_startup(self) -> None:
        Gtk.Application.do_startup(self)
        self.set_accels_for_action("app.quit", ["<Primary>q"])
        quit_action = Gio.SimpleAction.new("quit", None)
        quit_action.connect("activate", lambda *_: self.quit())
        self.add_action(quit_action)

    def do_activate(self) -> None:
        self.ensure_window()
        self.window.present()

    def do_open(self, files, n_files, hint) -> None:
        self.ensure_window()
        paths = [Path(file.get_path()) for file in files if file.get_path()]
        self.open_paths(paths)
        self.window.present()

    def ensure_window(self) -> None:
        if self.window is not None:
            return

        self.manager = WebKit.UserContentManager()
        self.manager.register_script_message_handler("mdviewer")
        self.manager.connect("script-message-received::mdviewer", self.on_script_message)

        shim = r"""
(() => {
  const callbacks = [];
  window.chrome = { webview: {
    postMessage(message) {
      window.webkit.messageHandlers.mdviewer.postMessage(message);
    },
    addEventListener(type, callback) {
      if (type === 'message') callbacks.push(callback);
    }
  }};
  window.__mdViewerReceive = message => {
    for (const callback of callbacks) callback({ data: message });
  };
})();
"""
        bridge = (APP_DIR / "bridge-upstream.js").read_text(encoding="utf-8")
        font_size = (APP_DIR / "font-size.js").read_text(encoding="utf-8")
        script = WebKit.UserScript.new(
            shim + "\n" + bridge + "\n" + font_size,
            WebKit.UserContentInjectedFrames.ALL_FRAMES,
            WebKit.UserScriptInjectionTime.START,
            None,
            None,
        )
        self.manager.add_script(script)

        self.webview = WebKit.WebView(user_content_manager=self.manager)
        settings = self.webview.get_settings()
        settings.set_enable_developer_extras(False)
        settings.set_enable_write_console_messages_to_stdout(False)
        self.webview.get_context().register_uri_scheme("mdviewer", self.handle_app_scheme)
        self.webview.get_context().register_uri_scheme("md-local", self.handle_asset_scheme)
        self.webview.connect("load-changed", self.on_load_changed)
        self.webview.connect("decide-policy", self.on_decide_policy)
        self._drop_target = Gtk.DropTarget.new(Gdk.FileList, Gdk.DragAction.COPY)
        self._drop_target.connect("drop", self.on_native_drop)
        self.webview.add_controller(self._drop_target)

        self.window = Gtk.ApplicationWindow(application=self, title="MD Viewer")
        self.window.set_default_size(1180, 760)
        self.window.set_size_request(560, 360)
        self.window.set_decorated(False)
        self.window.set_child(self.webview)
        self.webview.load_uri("mdviewer://app/index.html")
        self._poll_id = GLib.timeout_add(900, self.poll_documents)

    def on_native_drop(self, target, value, x, y) -> bool:
        try:
            paths = [Path(item.get_path()) for item in value.get_files() if item.get_path()]
            self.open_paths(paths)
            return bool(paths)
        except Exception as exc:
            self.post({"type": "app:error", "payload": str(exc)})
            return False

    def on_load_changed(self, webview, event) -> None:
        if event != WebKit.LoadEvent.FINISHED:
            return
        self.page_ready = True
        if self.pending_open:
            snapshots = self.pending_open[:]
            self.pending_open.clear()
            self.post({"type": "documents:opened", "payload": snapshots})

    def on_decide_policy(self, webview, decision, decision_type) -> bool:
        if decision_type == WebKit.PolicyDecisionType.NAVIGATION_ACTION:
            action = decision.get_navigation_action()
            request = action.get_request()
            uri = request.get_uri()
            if action.is_user_gesture() and urlparse(uri).scheme in {"http", "https"}:
                Gio.AppInfo.launch_default_for_uri(uri, None)
                decision.ignore()
                return True
        return False

    def handle_app_scheme(self, request) -> None:
        uri = urlparse(request.get_uri())
        relative = unquote(uri.path.lstrip("/")) or "index.html"
        target = (WEB_ROOT / relative).resolve()
        try:
            target.relative_to(WEB_ROOT.resolve())
            data = target.read_bytes()
        except (OSError, ValueError):
            request.finish_error(GLib.Error("Resource not found"))
            return
        mime = mimetypes.guess_type(target.name)[0] or "application/octet-stream"
        stream = Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(data))
        request.finish(stream, len(data), mime)

    def handle_asset_scheme(self, request) -> None:
        uri = urlparse(request.get_uri())
        doc_id = unquote(uri.path.lstrip("/"))
        relative = parse_qs(uri.query).get("path", [""])[0]
        doc = self.documents.get(doc_id)
        if not doc or not relative:
            request.finish_error(GLib.Error("Asset not found"))
            return
        try:
            base = doc.path.parent.resolve()
            candidate = (base / unquote(relative)).resolve()
            candidate.relative_to(base)
            if candidate.suffix.lower() not in IMAGE_EXTENSIONS:
                raise ValueError("Unsupported image type")
            if not candidate.is_file() or candidate.stat().st_size > MAX_BYTES:
                raise OSError("Invalid image")
            data = candidate.read_bytes()
        except (OSError, ValueError):
            request.finish_error(GLib.Error("Asset not found"))
            return
        mime = mimetypes.guess_type(candidate.name)[0] or "application/octet-stream"
        stream = Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(data))
        request.finish(stream, len(data), mime)

    def on_script_message(self, manager, value) -> None:
        try:
            message = json.loads(value.to_json(0))
        except Exception:
            return
        if message.get("kind") == "event":
            self.handle_event(message.get("method", ""), message.get("args", []))
            return
        if message.get("kind") != "invoke":
            return
        request_id = message.get("id")
        method = message.get("method", "")
        args = message.get("args", [])
        try:
            if method == "pickFiles":
                self.pick_files(request_id)
                return
            if method == "saveDocument":
                self.respond(request_id, self.save_document(str(args[0]), str(args[1])))
            elif method == "exportDocument":
                self.export_document(request_id, str(args[0]), str(args[1]))
                return
            elif method == "closeDocument":
                self.documents.pop(str(args[0]), None)
                self.respond(request_id, None)
            elif method == "getTheme":
                self.respond(request_id, self.get_theme())
            elif method == "setTheme":
                self.respond(request_id, self.set_theme(str(args[0])))
            elif method == "getFontSize":
                self.respond(request_id, self.get_font_size())
            elif method == "setFontSize":
                self.respond(request_id, self.set_font_size(args[0]))
            elif method == "openExternal":
                self.respond(request_id, self.open_external(str(args[0])))
            elif method == "windowAction":
                self.window_action(str(args[0]))
                self.respond(request_id, None)
            else:
                raise ValueError(f"未知方法：{method}")
        except Exception as exc:
            self.respond(request_id, error=str(exc))

    def handle_event(self, method: str, args: list) -> None:
        if method == "startWindowDrag":
            self.begin_move()
        elif method == "startWindowResize" and args:
            self.begin_resize(str(args[0]))

    def pick_files(self, request_id) -> None:
        dialog = Gtk.FileDialog(title="打开 Markdown 文件", modal=True)
        file_filter = Gtk.FileFilter()
        file_filter.set_name("Markdown (*.md)")
        file_filter.add_pattern("*.md")
        file_filter.add_pattern("*.MD")
        filters = Gio.ListStore.new(Gtk.FileFilter)
        filters.append(file_filter)
        dialog.set_filters(filters)
        dialog.set_default_filter(file_filter)

        def finished(source, result, _data=None):
            try:
                selected = source.open_multiple_finish(result)
                paths = [Path(selected.get_item(i).get_path()) for i in range(selected.get_n_items())]
                self.respond(request_id, self.load_paths(paths))
            except GLib.Error as exc:
                if exc.matches(Gtk.DialogError.quark(), Gtk.DialogError.DISMISSED):
                    self.respond(request_id, [])
                else:
                    self.respond(request_id, error=exc.message)
            except Exception as exc:
                self.respond(request_id, error=str(exc))

        dialog.open_multiple(self.window, None, finished)

    def export_document(self, request_id, doc_id: str, content: str) -> None:
        doc = self.documents.get(doc_id)
        if not doc:
            self.respond(request_id, error="文档未打开")
            return
        dialog = Gtk.FileDialog(title="导出 Word 文档", modal=True)
        dialog.set_initial_name(doc.path.stem + ".docx")
        dialog.set_initial_folder(Gio.File.new_for_path(str(doc.path.parent)))
        file_filter = Gtk.FileFilter()
        file_filter.set_name("Word 文档 (*.docx)")
        file_filter.add_pattern("*.docx")
        filters = Gio.ListStore.new(Gtk.FileFilter)
        filters.append(file_filter)
        dialog.set_filters(filters)
        dialog.set_default_filter(file_filter)

        def finished(source, result, _data=None):
            try:
                selected = source.save_finish(result)
                output = Path(selected.get_path())
                if output.suffix.lower() != ".docx":
                    output = output.with_suffix(".docx")
                export_docx(content, str(doc.path), str(output))
                self.respond(request_id, {"path": str(output), "name": output.name})
            except GLib.Error as exc:
                if exc.matches(Gtk.DialogError.quark(), Gtk.DialogError.DISMISSED):
                    self.respond(request_id, None)
                else:
                    self.respond(request_id, error=exc.message)
            except Exception as exc:
                self.respond(request_id, error=str(exc))

        dialog.save(self.window, None, finished)

    def open_paths(self, paths: list[Path]) -> None:
        snapshots = self.load_paths(paths)
        if not snapshots:
            return
        if self.page_ready:
            self.post({"type": "documents:opened", "payload": snapshots})
        else:
            self.pending_open.extend(snapshots)

    def load_paths(self, paths: list[Path]) -> list[dict]:
        snapshots = []
        for raw_path in paths:
            try:
                path = raw_path.expanduser().resolve()
                if path.suffix.lower() != ".md" or not path.is_file():
                    continue
                doc_id = hashlib.sha256(str(path).lower().encode("utf-8")).digest()[:10].hex()
                existing = self.documents.get(doc_id)
                if existing:
                    snapshots.append(existing.snapshot())
                    continue
                stat = path.stat()
                if stat.st_size > MAX_BYTES:
                    raise ValueError(f"{path.name} 超过 25MB，无法打开")
                content = path.read_text(encoding="utf-8-sig")
                doc = Document(doc_id, path, content, stat.st_mtime_ns)
                self.documents[doc_id] = doc
                snapshots.append(doc.snapshot())
            except Exception as exc:
                self.post({"type": "app:error", "payload": str(exc)})
        return snapshots

    def save_document(self, doc_id: str, content: str) -> dict:
        if len(content.encode("utf-8")) > MAX_BYTES:
            raise ValueError("文档超过 25MB，无法保存")
        doc = self.documents.get(doc_id)
        if not doc:
            raise ValueError("文档未打开")
        temp = doc.path.with_name(f".{doc.path.name}.mdviewer-{os.getpid()}.tmp")
        temp.write_text(content, encoding="utf-8", newline="")
        temp.replace(doc.path)
        stat = doc.path.stat()
        doc.content = content
        doc.mtime_ns = stat.st_mtime_ns
        doc.status = "ready"
        return doc.snapshot()

    def poll_documents(self) -> bool:
        for doc in list(self.documents.values()):
            try:
                stat = doc.path.stat()
                if stat.st_mtime_ns == doc.mtime_ns and doc.status == "ready":
                    continue
                if stat.st_size > MAX_BYTES:
                    raise ValueError("文件超过 25MB")
                content = doc.path.read_text(encoding="utf-8-sig")
                changed = content != doc.content or doc.status != "ready"
                doc.content = content
                doc.mtime_ns = stat.st_mtime_ns
                doc.status = "ready"
                if changed:
                    self.post({
                        "type": "documents:changed",
                        "payload": {"id": doc.id, "kind": "changed", "snapshot": doc.snapshot()},
                    })
            except FileNotFoundError:
                if doc.status != "deleted":
                    doc.status = "deleted"
                    self.post({
                        "type": "documents:changed",
                        "payload": {"id": doc.id, "kind": "deleted", "message": "源文件已被移动或删除"},
                    })
            except Exception as exc:
                if doc.status != "error":
                    doc.status = "error"
                    self.post({
                        "type": "documents:changed",
                        "payload": {"id": doc.id, "kind": "error", "message": str(exc)},
                    })
        return True

    def get_theme(self) -> str:
        try:
            value = self.theme_path().read_text(encoding="utf-8").strip()
            return value if value in {"system", "light", "dark"} else "system"
        except OSError:
            return "system"

    def set_theme(self, value: str) -> str:
        if value not in {"system", "light", "dark"}:
            raise ValueError("无效主题")
        path = self.theme_path()
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")
        return value

    def get_font_size(self) -> int:
        try:
            value = int(self.font_size_path().read_text(encoding="utf-8").strip())
            return value if 10 <= value <= 28 else 14
        except (OSError, ValueError):
            return 14

    def set_font_size(self, value) -> int:
        try:
            size = int(value)
        except (TypeError, ValueError):
            raise ValueError("无效字号")
        if not 10 <= size <= 28:
            raise ValueError("字号必须在 10 到 28 之间")
        path = self.font_size_path()
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(str(size), encoding="utf-8")
        return size

    @staticmethod
    def theme_path() -> Path:
        return Path(GLib.get_user_config_dir()) / "md-viewer" / "theme.txt"

    @staticmethod
    def font_size_path() -> Path:
        return Path(GLib.get_user_config_dir()) / "md-viewer" / "font-size.txt"

    @staticmethod
    def open_external(uri: str) -> bool:
        if urlparse(uri).scheme not in {"http", "https"}:
            return False
        Gio.AppInfo.launch_default_for_uri(uri, None)
        return True

    def window_action(self, action: str) -> None:
        if action == "minimize":
            self.window.minimize()
        elif action == "maximize":
            self.window.unmaximize() if self.window.is_maximized() else self.window.maximize()
        elif action == "close":
            self.window.close()

    def pointer_context(self):
        display = Gdk.Display.get_default()
        if not display:
            return None
        pointer = display.get_default_seat().get_pointer()
        if not pointer:
            return None
        result = pointer.get_surface_at_position()
        if not result or result[0] is None:
            return None
        return pointer, float(result[1]), float(result[2])

    def begin_move(self) -> None:
        context = self.pointer_context()
        surface = self.window.get_surface()
        if context and isinstance(surface, Gdk.Toplevel):
            pointer, x, y = context
            surface.begin_move(pointer, 1, x, y, Gdk.CURRENT_TIME)

    def begin_resize(self, direction: str) -> None:
        edges = {
            "left": Gdk.SurfaceEdge.WEST,
            "right": Gdk.SurfaceEdge.EAST,
            "top": Gdk.SurfaceEdge.NORTH,
            "top-left": Gdk.SurfaceEdge.NORTH_WEST,
            "top-right": Gdk.SurfaceEdge.NORTH_EAST,
            "bottom": Gdk.SurfaceEdge.SOUTH,
            "bottom-left": Gdk.SurfaceEdge.SOUTH_WEST,
            "bottom-right": Gdk.SurfaceEdge.SOUTH_EAST,
        }
        context = self.pointer_context()
        surface = self.window.get_surface()
        if context and direction in edges and isinstance(surface, Gdk.Toplevel):
            pointer, x, y = context
            surface.begin_resize(edges[direction], pointer, 1, x, y, Gdk.CURRENT_TIME)

    def respond(self, request_id, result=None, error: str | None = None) -> None:
        message = {"type": "response", "id": request_id, "result": result}
        if error:
            message["error"] = error
        self.post(message)

    def post(self, message: dict) -> None:
        if not self.webview or not self.page_ready and message.get("type") != "response":
            return
        payload = json.dumps(message, ensure_ascii=False).replace("</", "<\\/")
        script = f"window.__mdViewerReceive({payload});"
        self.webview.evaluate_javascript(script, -1, None, None, None, None)


def main() -> int:
    GLib.set_prgname(APP_ID)
    GLib.set_application_name("MD Viewer")
    app = MDViewer()
    return app.run(sys.argv)


if __name__ == "__main__":
    raise SystemExit(main())
