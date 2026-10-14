using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MDViewerLite;

internal sealed class DocumentStore : IDisposable
{
	private sealed class OpenDocument : IDisposable
	{
		public string Id = "";

		public string Name = "";

		public string FilePath = "";

		public string Content = "";

		public double MtimeMs;

		public string Status = "ready";

		public FileSystemWatcher? Watcher;

		public Timer? Debounce;

		public void Dispose()
		{
			Debounce?.Dispose();
			Watcher?.Dispose();
		}
	}

	private readonly Dictionary<string, OpenDocument> _documents = new Dictionary<string, OpenDocument>();

	private readonly object _sync = new object();

	public event Action<Dictionary<string, object>>? DocumentEvent;

	public List<object> OpenPaths(IEnumerable<string> paths)
	{
		List<object> list = new List<object>();
		foreach (string path in paths)
		{
			try
			{
				string fullPath = Path.GetFullPath(path);
				if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string key = IdForPath(fullPath);
				OpenDocument value;
				lock (_sync)
				{
					if (_documents.TryGetValue(key, out value))
					{
						list.Add(Snapshot(value));
						continue;
					}
					value = Load(fullPath);
					value.Watcher = CreateWatcher(value);
					_documents.Add(value.Id, value);
				}
				list.Add(Snapshot(value));
			}
			catch (Exception ex)
			{
				this.DocumentEvent?.Invoke(Message("app:error", ex.Message));
			}
		}
		return list;
	}

	public Dictionary<string, object> Save(string id, string content)
	{
		if (content.Length > 26214400)
		{
			throw new InvalidOperationException("文件超过 25MB，无法保存");
		}
		OpenDocument value;
		lock (_sync)
		{
			if (!_documents.TryGetValue(id, out value))
			{
				throw new InvalidOperationException("文档已关闭");
			}
		}
		File.WriteAllText(value.FilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		OpenDocument openDocument = Load(value.FilePath);
		lock (_sync)
		{
			value.Content = openDocument.Content;
			value.MtimeMs = openDocument.MtimeMs;
			value.Status = "ready";
		}
		return Snapshot(value);
	}

	public string GetFilePath(string id)
	{
		lock (_sync)
		{
			if (!_documents.TryGetValue(id, out OpenDocument value))
			{
				throw new InvalidOperationException("文档已关闭");
			}
			return value.FilePath;
		}
	}

	public void Close(string id)
	{
		OpenDocument value = null;
		lock (_sync)
		{
			if (_documents.TryGetValue(id, out value))
			{
				_documents.Remove(id);
			}
		}
		value?.Dispose();
	}

	public string? ResolveAsset(string id, string relativePath)
	{
		OpenDocument value;
		lock (_sync)
		{
			if (!_documents.TryGetValue(id, out value))
			{
				return null;
			}
		}
		if (Path.IsPathRooted(relativePath))
		{
			return null;
		}
		string text = Path.GetDirectoryName(value.FilePath) ?? "";
		string value2 = Path.GetFullPath(text).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, relativePath));
		if (!fullPath.StartsWith(value2, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (!new string[9] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".avif" }.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase) || !File.Exists(fullPath))
		{
			return null;
		}
		return fullPath;
	}

	private static OpenDocument Load(string filePath)
	{
		FileInfo fileInfo = new FileInfo(filePath);
		string content = File.ReadAllText(filePath, Encoding.UTF8).TrimStart('\ufeff');
		return new OpenDocument
		{
			Id = IdForPath(filePath),
			Name = Path.GetFileName(filePath),
			FilePath = filePath,
			Content = content,
			MtimeMs = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
			Status = "ready"
		};
	}

	private FileSystemWatcher CreateWatcher(OpenDocument document)
	{
		FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(Path.GetDirectoryName(document.FilePath) ?? ".", Path.GetFileName(document.FilePath));
		fileSystemWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite;
		fileSystemWatcher.EnableRaisingEvents = true;
		fileSystemWatcher.Changed += delegate
		{
			ScheduleRefresh(document);
		};
		fileSystemWatcher.Created += delegate
		{
			ScheduleRefresh(document);
		};
		fileSystemWatcher.Deleted += delegate
		{
			MarkUnavailable(document, "deleted", "文件已被移动或删除，当前显示的是最后读取的内容。");
		};
		fileSystemWatcher.Renamed += delegate
		{
			MarkUnavailable(document, "deleted", "文件已被移动或重命名，当前显示的是最后读取的内容。");
		};
		fileSystemWatcher.Error += delegate(object _, ErrorEventArgs args)
		{
			MarkUnavailable(document, "error", "无法继续监听文件：" + args.GetException().Message);
		};
		return fileSystemWatcher;
	}

	private void ScheduleRefresh(OpenDocument document)
	{
		lock (_sync)
		{
			document.Debounce?.Dispose();
			document.Debounce = new Timer(delegate
			{
				Refresh(document);
			}, null, 220, -1);
		}
	}

	private void Refresh(OpenDocument document)
	{
		try
		{
			OpenDocument openDocument = Load(document.FilePath);
			lock (_sync)
			{
				if (!_documents.ContainsKey(document.Id))
				{
					return;
				}
				document.Content = openDocument.Content;
				document.MtimeMs = openDocument.MtimeMs;
				document.Status = "ready";
			}
			this.DocumentEvent?.Invoke(Message("documents:changed", new Dictionary<string, object>
			{
				["id"] = document.Id,
				["kind"] = "changed",
				["snapshot"] = Snapshot(document)
			}));
		}
		catch (Exception ex)
		{
			MarkUnavailable(document, "error", "文件刷新失败：" + ex.Message);
		}
	}

	private void MarkUnavailable(OpenDocument document, string status, string text)
	{
		lock (_sync)
		{
			if (!_documents.ContainsKey(document.Id))
			{
				return;
			}
			document.Status = status;
		}
		this.DocumentEvent?.Invoke(Message("documents:changed", new Dictionary<string, object>
		{
			["id"] = document.Id,
			["kind"] = status,
			["message"] = text
		}));
	}

	private static Dictionary<string, object> Snapshot(OpenDocument document)
	{
		return new Dictionary<string, object>
		{
			["id"] = document.Id,
			["name"] = document.Name,
			["content"] = document.Content,
			["mtimeMs"] = document.MtimeMs,
			["status"] = document.Status
		};
	}

	private static Dictionary<string, object> Message(string type, object payload)
	{
		return new Dictionary<string, object>
		{
			["type"] = type,
			["payload"] = payload
		};
	}

	private static string IdForPath(string filePath)
	{
		using SHA256 sHA = SHA256.Create();
		return string.Concat(from value in sHA.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath).ToLowerInvariant())).Take(10)
			select value.ToString("x2"));
	}

	public void Dispose()
	{
		lock (_sync)
		{
			foreach (OpenDocument value in _documents.Values)
			{
				value.Dispose();
			}
			_documents.Clear();
		}
	}
}
