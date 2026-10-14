#!/usr/bin/python3
"""Small, dependency-free Markdown to DOCX exporter for MD Viewer."""

from __future__ import annotations

import html
import mimetypes
import re
import zipfile
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import unquote, urlparse


W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"


def esc(value: str) -> str:
    return html.escape(value, quote=True)


def run(text: str, *, bold=False, italic=False, strike=False, code=False) -> str:
    props = []
    if bold:
        props.append("<w:b/>")
    if italic:
        props.append("<w:i/>")
    if strike:
        props.append("<w:strike/>")
    if code:
        props.append('<w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/>'
                     '<w:sz w:val="19"/><w:shd w:val="clear" w:fill="F2F4F7"/>')
    prop_xml = f"<w:rPr>{''.join(props)}</w:rPr>" if props else ""
    return f'<w:r>{prop_xml}<w:t xml:space="preserve">{esc(text)}</w:t></w:r>'


@dataclass
class ExportContext:
    source_path: Path
    relationships: list[str] = field(default_factory=list)
    media: list[tuple[str, bytes, str]] = field(default_factory=list)
    image_index: int = 0

    def relationship(self, rel_type: str, target: str, external=False) -> str:
        rid = f"rId{len(self.relationships) + 3}"
        mode = ' TargetMode="External"' if external else ""
        self.relationships.append(
            f'<Relationship Id="{rid}" Type="{rel_type}" Target="{esc(target)}"{mode}/>'
        )
        return rid

    def image(self, raw_target: str, alt: str) -> str | None:
        parsed = urlparse(raw_target)
        if parsed.scheme in {"http", "https", "data"}:
            return None
        target = Path(unquote(parsed.path or raw_target))
        if not target.is_absolute():
            target = (self.source_path.parent / target).resolve()
        try:
            if not target.is_file() or target.stat().st_size > 25 * 1024 * 1024:
                return None
            data = target.read_bytes()
        except OSError:
            return None

        ext = target.suffix.lower().lstrip(".") or "png"
        if ext == "jpg":
            ext = "jpeg"
        content_type = mimetypes.types_map.get(f".{ext}", f"image/{ext}")
        self.image_index += 1
        name = f"image{self.image_index}.{ext}"
        self.media.append((name, data, content_type))
        rid = self.relationship(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
            f"media/{name}",
        )

        width, height = _image_size(target)
        max_width = 5_669_280  # 6.2 inches in EMUs
        cx = min(max_width, int(width / 96 * 914_400))
        cy = max(1, int(cx * height / max(width, 1)))
        return (
            '<w:p><w:pPr><w:spacing w:before="100" w:after="140"/>'
            '<w:jc w:val="center"/></w:pPr><w:r><w:drawing>'
            '<wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" '
            'distT="0" distB="0" distL="0" distR="0">'
            f'<wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="{self.image_index}" '
            f'name="{esc(name)}" descr="{esc(alt)}"/>'
            '<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">'
            '<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
            '<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">'
            f'<pic:nvPicPr><pic:cNvPr id="0" name="{esc(name)}"/><pic:cNvPicPr/></pic:nvPicPr>'
            f'<pic:blipFill><a:blip r:embed="{rid}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
            f'<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>'
            '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>'
            '</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>'
        )


def _image_size(path: Path) -> tuple[int, int]:
    try:
        import gi

        gi.require_version("GdkPixbuf", "2.0")
        from gi.repository import GdkPixbuf

        pixbuf = GdkPixbuf.Pixbuf.new_from_file(str(path))
        return max(1, pixbuf.get_width()), max(1, pixbuf.get_height())
    except Exception:
        return 800, 600


INLINE_TOKEN = re.compile(
    r"(!?\[[^\]]*\]\([^\)]+\)|\*\*[^*]+\*\*|__[^_]+__|~~[^~]+~~|`[^`]+`|\*[^*]+\*)"
)


def inline_runs(text: str, ctx: ExportContext) -> str:
    output: list[str] = []
    cursor = 0
    for match in INLINE_TOKEN.finditer(text):
        if match.start() > cursor:
            output.append(run(text[cursor:match.start()]))
        token = match.group(0)
        link = re.fullmatch(r"\[([^\]]+)\]\(([^\s\)]+)(?:\s+['\"].*?['\"])?\)", token)
        if link:
            label, target = link.groups()
            parsed = urlparse(target)
            if parsed.scheme in {"http", "https"}:
                rid = ctx.relationship(
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                    target,
                    True,
                )
                output.append(
                    f'<w:hyperlink r:id="{rid}"><w:r><w:rPr><w:color w:val="3978B8"/>'
                    f'<w:u w:val="single"/></w:rPr><w:t xml:space="preserve">{esc(label)}</w:t>'
                    "</w:r></w:hyperlink>"
                )
            else:
                output.append(run(label))
        elif token.startswith("!["):
            output.append(run(re.sub(r"!\[([^\]]*)\].*", r"[\1]", token)))
        elif token.startswith(("**", "__")):
            output.append(run(token[2:-2], bold=True))
        elif token.startswith("~~"):
            output.append(run(token[2:-2], strike=True))
        elif token.startswith("`"):
            output.append(run(token[1:-1], code=True))
        elif token.startswith("*"):
            output.append(run(token[1:-1], italic=True))
        else:
            output.append(run(token))
        cursor = match.end()
    if cursor < len(text):
        output.append(run(text[cursor:]))
    return "".join(output)


def paragraph(text: str, ctx: ExportContext, style: str | None = None) -> str:
    props = f'<w:pStyle w:val="{style}"/>' if style else ""
    return f"<w:p><w:pPr>{props}</w:pPr>{inline_runs(text, ctx)}</w:p>"


def code_block(lines: list[str]) -> str:
    rows = []
    for idx, line in enumerate(lines):
        if idx:
            rows.append("<w:r><w:br/></w:r>")
        rows.append(run(line, code=True))
    return (
        '<w:p><w:pPr><w:spacing w:before="100" w:after="160"/>'
        '<w:shd w:val="clear" w:fill="F2F4F7"/><w:ind w:left="160" w:right="160"/>'
        f"</w:pPr>{''.join(rows)}</w:p>"
    )


def split_table_row(line: str) -> list[str]:
    line = line.strip()
    if line.startswith("|"):
        line = line[1:]
    if line.endswith("|") and not line.endswith(r"\|"):
        line = line[:-1]
    return [part.strip().replace(r"\|", "|") for part in re.split(r"(?<!\\)\|", line)]


def table(rows: list[list[str]], ctx: ExportContext) -> str:
    if not rows:
        return ""
    cols = max(len(row) for row in rows)
    width = max(900, 9000 // max(cols, 1))
    grid = "".join(f'<w:gridCol w:w="{width}"/>' for _ in range(cols))
    row_xml = []
    for row_index, row in enumerate(rows):
        cells = []
        for col in range(cols):
            text = row[col] if col < len(row) else ""
            shade = '<w:shd w:val="clear" w:fill="E8EEF5"/>' if row_index == 0 else ""
            content = inline_runs(text, ctx)
            if row_index == 0:
                content = f"<w:r><w:rPr><w:b/></w:rPr><w:t>{esc(text)}</w:t></w:r>"
            cells.append(
                f'<w:tc><w:tcPr><w:tcW w:w="{width}" w:type="dxa"/>{shade}</w:tcPr>'
                f'<w:p><w:pPr><w:spacing w:before="0" w:after="0"/></w:pPr>{content}</w:p></w:tc>'
            )
        row_xml.append(f"<w:tr>{''.join(cells)}</w:tr>")
    borders = "".join(
        f'<w:{side} w:val="single" w:sz="4" w:color="D9E2EC"/>'
        for side in ("top", "left", "bottom", "right", "insideH", "insideV")
    )
    return (
        '<w:tbl><w:tblPr><w:tblW w:w="9000" w:type="dxa"/><w:tblLayout w:type="fixed"/>'
        f"<w:tblBorders>{borders}</w:tblBorders></w:tblPr><w:tblGrid>{grid}</w:tblGrid>"
        f"{''.join(row_xml)}</w:tbl><w:p/>"
    )


def build_body(markdown: str, ctx: ExportContext) -> str:
    lines = markdown.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    out: list[str] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if not line.strip():
            i += 1
            continue
        fence = re.match(r"^\s*```\s*([^`]*)$", line)
        if fence:
            i += 1
            code: list[str] = []
            while i < len(lines) and not re.match(r"^\s*```\s*$", lines[i]):
                code.append(lines[i])
                i += 1
            i += i < len(lines)
            out.append(code_block(code))
            continue
        heading = re.match(r"^(#{1,6})\s+(.+?)\s*#*\s*$", line)
        if heading:
            level = min(len(heading.group(1)), 3)
            out.append(paragraph(heading.group(2), ctx, f"Heading{level}"))
            i += 1
            continue
        if re.match(r"^\s*((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})\s*$", line):
            out.append('<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" '
                       'w:space="6" w:color="CBD5E1"/></w:pBdr></w:pPr></w:p>')
            i += 1
            continue
        image = re.match(r'^\s*!\[([^\]]*)\]\(([^\s\)]+)(?:\s+["\'].*?["\'])?\)\s*$', line)
        if image:
            out.append(ctx.image(image.group(2), image.group(1)) or paragraph(f"[{image.group(1)}]", ctx))
            i += 1
            continue
        if i + 1 < len(lines) and "|" in line and re.match(
            r"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", lines[i + 1]
        ):
            rows = [split_table_row(line)]
            i += 2
            while i < len(lines) and "|" in lines[i] and lines[i].strip():
                rows.append(split_table_row(lines[i]))
                i += 1
            out.append(table(rows, ctx))
            continue
        if re.match(r"^\s*>", line):
            quote_lines = []
            while i < len(lines) and re.match(r"^\s*>", lines[i]):
                quote_lines.append(re.sub(r"^\s*>\s?", "", lines[i]))
                i += 1
            out.append(paragraph(" ".join(quote_lines), ctx, "Quote"))
            continue
        item = re.match(r"^(\s*)([-+*]|\d+[.)])\s+(.+)$", line)
        if item:
            indent, marker, text = item.groups()
            level = min(8, len(indent.expandtabs(4)) // 2)
            num_id = 2 if marker[0].isdigit() else 1
            task = re.match(r"^\[([ xX])\]\s+(.*)$", text)
            if task:
                text = ("☒ " if task.group(1).lower() == "x" else "☐ ") + task.group(2)
            out.append(
                f'<w:p><w:pPr><w:numPr><w:ilvl w:val="{level}"/><w:numId w:val="{num_id}"/>'
                f"</w:numPr></w:pPr>{inline_runs(text, ctx)}</w:p>"
            )
            i += 1
            continue

        para = [line.strip()]
        i += 1
        while i < len(lines) and lines[i].strip() and not re.match(
            r"^(#{1,6})\s+|^\s*```|^\s*>|^(\s*)([-+*]|\d+[.)])\s+|^\s*!\[", lines[i]
        ):
            if "|" in lines[i] and i + 1 < len(lines) and re.match(r"^\s*\|?\s*:?-{3,}", lines[i + 1]):
                break
            para.append(lines[i].strip())
            i += 1
        out.append(paragraph(" ".join(para), ctx))
    return "".join(out)


def styles() -> str:
    headings = []
    for level, size, color in ((1, 32, "2E74B5"), (2, 26, "2E74B5"), (3, 24, "1F4D78")):
        headings.append(
            f'<w:style w:type="paragraph" w:styleId="Heading{level}"><w:name w:val="heading {level}"/>'
            f'<w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/>'
            f'<w:outlineLvl w:val="{level - 1}"/></w:pPr><w:rPr><w:b/><w:color w:val="{color}"/>'
            f'<w:sz w:val="{size}"/></w:rPr></w:style>'
        )
    return (
        f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="{W}">'
        '<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" '
        'w:eastAsia="Microsoft YaHei UI"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>'
        '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/>'
        '<w:qFormat/><w:pPr><w:spacing w:after="120" w:line="290" w:lineRule="auto"/></w:pPr></w:style>'
        + "".join(headings)
        + '<w:style w:type="paragraph" w:styleId="Quote"><w:name w:val="Quote"/><w:basedOn w:val="Normal"/>'
          '<w:pPr><w:ind w:left="360"/><w:pBdr><w:left w:val="single" w:sz="18" w:space="8" '
          'w:color="89A9D8"/></w:pBdr></w:pPr><w:rPr><w:i/><w:color w:val="52606D"/></w:rPr></w:style>'
        '</w:styles>'
    )


def numbering() -> str:
    levels = []
    for abstract, kind in ((0, "bullet"), (1, "decimal")):
        level_xml = []
        for level in range(9):
            fmt = "bullet" if kind == "bullet" else "decimal"
            text = ("•" if kind == "bullet" else f"%{level + 1}.")
            left = 720 + level * 360
            level_xml.append(
                f'<w:lvl w:ilvl="{level}"><w:start w:val="1"/><w:numFmt w:val="{fmt}"/>'
                f'<w:lvlText w:val="{text}"/><w:pPr><w:ind w:left="{left}" w:hanging="270"/></w:pPr></w:lvl>'
            )
        levels.append(f'<w:abstractNum w:abstractNumId="{abstract}">{"".join(level_xml)}</w:abstractNum>')
    return (
        f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:numbering xmlns:w="{W}">'
        + "".join(levels)
        + '<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>'
          '<w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num></w:numbering>'
    )


def export(markdown: str, source_path: str, output_path: str) -> None:
    source = Path(source_path).resolve()
    output = Path(output_path)
    ctx = ExportContext(source)
    body = build_body(markdown, ctx)
    section = ('<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" '
               'w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>')
    document = (
        f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{W}" xmlns:r="{R}">'
        f"<w:body>{body}{section}</w:body></w:document>"
    )
    defaults = {'rels': 'application/vnd.openxmlformats-package.relationships+xml', 'xml': 'application/xml'}
    for name, _, content_type in ctx.media:
        defaults[name.rsplit('.', 1)[-1]] = content_type
    content_types = ''.join(f'<Default Extension="{esc(ext)}" ContentType="{esc(ctype)}"/>' for ext, ctype in defaults.items())
    content_types += (
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
        '<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>'
        '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>'
        '<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>'
    )
    package_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>'
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>'
        '<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>'
        '</Relationships>'
    )
    doc_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>'
        + ''.join(ctx.relationships) + '</Relationships>'
    )
    now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    core = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
        'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
        f'<dc:title>{esc(source.stem)}</dc:title><dc:creator>MD Viewer</dc:creator>'
        f'<dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created>'
        f'<dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified></cp:coreProperties>'
    )
    app = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
           '<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">'
           '<Application>MD Viewer</Application><AppVersion>2.2.0</AppVersion></Properties>')

    output.parent.mkdir(parents=True, exist_ok=True)
    temp = output.with_name(output.name + ".tmp")
    with zipfile.ZipFile(temp, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("[Content_Types].xml", f'<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">{content_types}</Types>')
        archive.writestr("_rels/.rels", package_rels)
        archive.writestr("word/document.xml", document)
        archive.writestr("word/styles.xml", styles())
        archive.writestr("word/numbering.xml", numbering())
        archive.writestr("word/_rels/document.xml.rels", doc_rels)
        archive.writestr("docProps/core.xml", core)
        archive.writestr("docProps/app.xml", app)
        for name, data, _ in ctx.media:
            archive.writestr(f"word/media/{name}", data)
    temp.replace(output)

