using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace MDViewerLite;

internal static class MarkdownDocxExporter
{
	private sealed class Relationship
	{
		public string Id = "";

		public string Type = "";

		public string Target = "";

		public bool External;
	}

	private sealed class MediaPart
	{
		public string Name = "";

		public string ContentType = "";

		public byte[] Bytes = Array.Empty<byte>();
	}

	private sealed class ExportContext
	{
		public readonly List<Relationship> Relationships = new List<Relationship>();

		public readonly List<MediaPart> Media = new List<MediaPart>();

		public readonly string SourceDirectory;

		public int NextRelationship = 1;

		public int NextDrawing = 1;

		public ExportContext(string sourcePath)
		{
			SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "";
		}

		public string AddRelationship(string type, string target, bool external = false)
		{
			string text = "rId" + NextRelationship++;
			Relationships.Add(new Relationship
			{
				Id = text,
				Type = type,
				Target = target,
				External = external
			});
			return text;
		}
	}

	private sealed class InlineStyle
	{
		public bool Bold;

		public bool Italic;

		public bool Strike;

		public bool Code;

		public InlineStyle Clone()
		{
			return new InlineStyle
			{
				Bold = Bold,
				Italic = Italic,
				Strike = Strike,
				Code = Code
			};
		}
	}

	private const int PageWidthDxa = 12240;

	private const int PageHeightDxa = 15840;

	private const int ContentWidthDxa = 9360;

	private const long EmusPerPixel = 9525L;

	private const long MaxImageWidthEmu = 5760720L;

	private const long MaxImageHeightEmu = 7315200L;

	private const string XmlHeader = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

	public static void Export(string markdown, string sourcePath, string outputPath)
	{
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Expected O, but got Unknown
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			throw new ArgumentException("缺少导出路径", "outputPath");
		}
		if (!string.Equals(Path.GetExtension(outputPath), ".docx", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("导出文件必须使用 .docx 扩展名");
		}
		if (markdown.Length > 26214400)
		{
			throw new InvalidOperationException("Markdown 内容超过 25MB，无法导出");
		}
		ExportContext exportContext = new ExportContext(sourcePath);
		string text = BuildDocumentBody((markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n'), exportContext);
		string content = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>" + text + SectionProperties() + "</w:body></w:document>";
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(outputPath));
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text2 = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				ZipArchive val = new ZipArchive((Stream)fileStream, (ZipArchiveMode)1);
				try
				{
					WriteText(val, "[Content_Types].xml", ContentTypes(exportContext));
					WriteText(val, "_rels/.rels", PackageRelationships());
					WriteText(val, "docProps/core.xml", CoreProperties(Path.GetFileNameWithoutExtension(sourcePath)));
					WriteText(val, "docProps/app.xml", AppProperties());
					WriteText(val, "word/document.xml", content);
					WriteText(val, "word/styles.xml", Styles());
					WriteText(val, "word/numbering.xml", Numbering());
					WriteText(val, "word/_rels/document.xml.rels", DocumentRelationships(exportContext));
					foreach (MediaPart medium in exportContext.Media)
					{
						WriteBytes(val, "word/media/" + medium.Name, medium.Bytes);
					}
				}
				finally
				{
					((IDisposable)val)?.Dispose();
				}
			}
			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
			File.Move(text2, outputPath);
		}
		finally
		{
			try
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			catch
			{
			}
		}
	}

	private static string BuildDocumentBody(string markdown, ExportContext context)
	{
		string[] array = markdown.Split('\n');
		StringBuilder stringBuilder = new StringBuilder();
		int i = 0;
		while (i < array.Length)
		{
			string text = array[i];
			if (string.IsNullOrWhiteSpace(text))
			{
				i++;
				continue;
			}
			Match match = Regex.Match(text, "^\\s*```\\s*([^`]*)$");
			if (match.Success)
			{
				string language = match.Groups[1].Value.Trim();
				i++;
				List<string> list = new List<string>();
				while (i < array.Length && !Regex.IsMatch(array[i], "^\\s*```\\s*$"))
				{
					list.Add(array[i++]);
				}
				if (i < array.Length)
				{
					i++;
				}
				stringBuilder.Append(CodeBlock(list, language));
				continue;
			}
			Match match2 = Regex.Match(text, "^(#{1,6})\\s+(.+?)\\s*#*\\s*$");
			if (match2.Success)
			{
				int num = Math.Min(3, match2.Groups[1].Value.Length);
				stringBuilder.Append(Paragraph(match2.Groups[2].Value, "Heading" + num, context));
				i++;
				continue;
			}
			if (Regex.IsMatch(text, "^\\s*((\\*\\s*){3,}|(-\\s*){3,}|(_\\s*){3,})\\s*$"))
			{
				stringBuilder.Append("<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"6\" w:color=\"CBD5E1\"/></w:pBdr><w:spacing w:before=\"120\" w:after=\"160\"/></w:pPr></w:p>");
				i++;
				continue;
			}
			if (i + 1 < array.Length && LooksLikeTableRow(text) && IsTableSeparator(array[i + 1]))
			{
				List<string[]> list2 = new List<string[]> { SplitTableRow(text) };
				i += 2;
				while (i < array.Length && LooksLikeTableRow(array[i]) && !string.IsNullOrWhiteSpace(array[i]))
				{
					list2.Add(SplitTableRow(array[i++]));
				}
				stringBuilder.Append(Table(list2, context));
				continue;
			}
			Match match3 = Regex.Match(text, "^\\s*!\\[([^\\]]*)\\]\\(([^\\s\\)]+)(?:\\s+[\"'].*?[\"'])?\\)\\s*$");
			if (match3.Success)
			{
				string text2 = ImageParagraph(match3.Groups[2].Value, match3.Groups[1].Value, context);
				stringBuilder.Append(text2 ?? Paragraph("[图片：" + (string.IsNullOrWhiteSpace(match3.Groups[1].Value) ? match3.Groups[2].Value : match3.Groups[1].Value) + "]", null, context));
				i++;
			}
			else if (Regex.IsMatch(text, "^\\s*>"))
			{
				List<string> list3 = new List<string>();
				while (i < array.Length && Regex.IsMatch(array[i], "^\\s*>"))
				{
					list3.Add(Regex.Replace(array[i++], "^\\s*>\\s?", ""));
				}
				stringBuilder.Append(Paragraph(string.Join(" ", list3), "Quote", context));
			}
			else if (Regex.Match(text, "^(\\s*)([-+*]|\\d+[.)])\\s+(.+)$").Success)
			{
				for (; i < array.Length; i++)
				{
					Match match4 = Regex.Match(array[i], "^(\\s*)([-+*]|\\d+[.)])\\s+(.+)$");
					if (!match4.Success)
					{
						break;
					}
					int level = Math.Min(2, match4.Groups[1].Value.Replace("\t", "    ").Length / 2);
					bool flag = char.IsDigit(match4.Groups[2].Value[0]);
					string text3 = match4.Groups[3].Value;
					Match match5 = Regex.Match(text3, "^\\[([ xX])\\]\\s+(.*)$");
					if (match5.Success)
					{
						text3 = ((match5.Groups[1].Value == " ") ? "☐ " : "☒ ") + match5.Groups[2].Value;
					}
					stringBuilder.Append(ListParagraph(text3, (!flag) ? 1 : 2, level, context));
				}
			}
			else
			{
				List<string> list4 = new List<string>();
				while (i < array.Length && !string.IsNullOrWhiteSpace(array[i]) && (list4.Count <= 0 || !IsBlockStart(array, i)))
				{
					list4.Add(array[i++]);
				}
				string input = string.Join(" ", list4.Select((string value) => value.Trim()));
				input = Regex.Replace(input, "<[^>]+>", "");
				if (input.Length > 0)
				{
					stringBuilder.Append(Paragraph(input, null, context));
				}
			}
		}
		return stringBuilder.ToString();
	}

	private static bool IsBlockStart(string[] lines, int index)
	{
		string text = lines[index];
		if (!Regex.IsMatch(text, "^\\s*```") && !Regex.IsMatch(text, "^#{1,6}\\s+") && !Regex.IsMatch(text, "^\\s*>") && !Regex.IsMatch(text, "^(\\s*)([-+*]|\\d+[.)])\\s+") && !Regex.IsMatch(text, "^\\s*!\\[[^\\]]*\\]\\(") && !Regex.IsMatch(text, "^\\s*((\\*\\s*){3,}|(-\\s*){3,}|(_\\s*){3,})\\s*$"))
		{
			if (index + 1 < lines.Length && LooksLikeTableRow(text))
			{
				return IsTableSeparator(lines[index + 1]);
			}
			return false;
		}
		return true;
	}

	private static string Paragraph(string text, string? style, ExportContext context)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (!string.IsNullOrEmpty(style))
		{
			stringBuilder.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
		}
		return "<w:p>" + ((stringBuilder.Length > 0) ? ("<w:pPr>" + stringBuilder?.ToString() + "</w:pPr>") : "") + InlineRuns(text, context) + "</w:p>";
	}

	private static string ListParagraph(string text, int numberId, int level, ExportContext context)
	{
		return "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"" + level + "\"/><w:numId w:val=\"" + numberId + "\"/></w:numPr></w:pPr>" + InlineRuns(text, context) + "</w:p>";
	}

	private static string CodeBlock(IEnumerable<string> lines, string language)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(language))
		{
			stringBuilder.Append(Run(language + "\n", new InlineStyle
			{
				Bold = true,
				Code = true
			}));
		}
		bool flag = true;
		foreach (string line in lines)
		{
			if (!flag)
			{
				stringBuilder.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:eastAsia=\"Microsoft YaHei UI\"/><w:sz w:val=\"19\"/></w:rPr><w:br/></w:r>");
			}
			stringBuilder.Append(Run((line.Length == 0) ? " " : line, new InlineStyle
			{
				Code = true
			}));
			flag = false;
		}
		return "<w:p><w:pPr><w:spacing w:before=\"100\" w:after=\"160\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F2F4F7\"/><w:ind w:left=\"160\" w:right=\"160\"/></w:pPr>" + stringBuilder?.ToString() + "</w:p>";
	}

	private static string Table(IReadOnlyList<string[]> rows, ExportContext context)
	{
		int columns = Math.Max(1, rows.Max((string[] row) => row.Length));
		int baseWidth = 9360 / columns;
		int[] array = (from index in Enumerable.Range(0, columns)
			select (index != columns - 1) ? baseWidth : (9360 - baseWidth * (columns - 1))).ToArray();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(9360).Append("\" w:type=\"dxa\"/><w:tblInd w:w=\"120\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>")
			.Append("<w:top w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"D9E2EC\"/>")
			.Append("</w:tblBorders><w:tblCellMar><w:top w:w=\"80\" w:type=\"dxa\"/><w:left w:w=\"120\" w:type=\"dxa\"/><w:bottom w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"120\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
		int[] array2 = array;
		foreach (int value in array2)
		{
			stringBuilder.Append("<w:gridCol w:w=\"").Append(value).Append("\"/>");
		}
		stringBuilder.Append("</w:tblGrid>");
		for (int num2 = 0; num2 < rows.Count; num2++)
		{
			stringBuilder.Append("<w:tr>");
			for (int num3 = 0; num3 < columns; num3++)
			{
				string text = ((num3 < rows[num2].Length) ? rows[num2][num3] : "");
				stringBuilder.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(array[num3]).Append("\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/>");
				if (num2 == 0)
				{
					stringBuilder.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E8EEF5\"/>");
				}
				stringBuilder.Append("</w:tcPr><w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
				if (num2 == 0)
				{
					stringBuilder.Append("<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">").Append(Escape(StripInlineMarkers(text))).Append("</w:t></w:r>");
				}
				else
				{
					stringBuilder.Append(InlineRuns(text, context));
				}
				stringBuilder.Append("</w:p></w:tc>");
			}
			stringBuilder.Append("</w:tr>");
		}
		stringBuilder.Append("</w:tbl><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:p>");
		return stringBuilder.ToString();
	}

	private static bool LooksLikeTableRow(string line)
	{
		if (line.Contains("|"))
		{
			return SplitTableRow(line).Length >= 2;
		}
		return false;
	}

	private static bool IsTableSeparator(string line)
	{
		return Regex.IsMatch(line, "^\\s*\\|?\\s*:?-{3,}:?\\s*(\\|\\s*:?-{3,}:?\\s*)+\\|?\\s*$");
	}

	private static string[] SplitTableRow(string line)
	{
		string text = line.Trim();
		if (text.StartsWith("|"))
		{
			text = text.Substring(1);
		}
		if (text.EndsWith("|"))
		{
			text = text.Substring(0, text.Length - 1);
		}
		return (from cell in Regex.Split(text, "(?<!\\\\)\\|")
			select cell.Trim().Replace("\\|", "|")).ToArray();
	}

	private static string InlineRuns(string markdown, ExportContext context)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = HttpUtility.HtmlDecode(markdown);
		ParseInline(text, 0, text.Length, new InlineStyle(), stringBuilder, context);
		return stringBuilder.ToString();
	}

	private static void ParseInline(string text, int start, int end, InlineStyle style, StringBuilder output, ExportContext context)
	{
		StringBuilder plain = new StringBuilder();
		Action action = delegate
		{
			if (plain.Length > 0)
			{
				output.Append(Run(plain.ToString(), style));
				plain.Clear();
			}
		};
		int num = start;
		while (num < end)
		{
			if (text[num] == '\\' && num + 1 < end)
			{
				plain.Append(text[num + 1]);
				num += 2;
				continue;
			}
			if (text[num] == '`')
			{
				int num2 = text.IndexOf('`', num + 1);
				if (num2 > num && num2 < end)
				{
					action();
					InlineStyle inlineStyle = style.Clone();
					inlineStyle.Code = true;
					output.Append(Run(text.Substring(num + 1, num2 - num - 1), inlineStyle));
					num = num2 + 1;
					continue;
				}
			}
			if (num + 1 < end && (text.Substring(num, 2) == "**" || text.Substring(num, 2) == "__" || text.Substring(num, 2) == "~~"))
			{
				string text2 = text.Substring(num, 2);
				int num3 = text.IndexOf(text2, num + 2, StringComparison.Ordinal);
				if (num3 > num && num3 < end)
				{
					action();
					InlineStyle inlineStyle2 = style.Clone();
					if (text2 == "~~")
					{
						inlineStyle2.Strike = true;
					}
					else
					{
						inlineStyle2.Bold = true;
					}
					ParseInline(text, num + 2, num3, inlineStyle2, output, context);
					num = num3 + 2;
					continue;
				}
			}
			if (text[num] == '*' || text[num] == '_')
			{
				int num4 = text.IndexOf(text[num], num + 1);
				if (num4 > num + 1 && num4 < end)
				{
					action();
					InlineStyle inlineStyle3 = style.Clone();
					inlineStyle3.Italic = true;
					ParseInline(text, num + 1, num4, inlineStyle3, output, context);
					num = num4 + 1;
					continue;
				}
			}
			if (text[num] == '[')
			{
				int num5 = text.IndexOf(']', num + 1);
				if (num5 > num && num5 + 1 < end && text[num5 + 1] == '(')
				{
					int num6 = text.IndexOf(')', num5 + 2);
					if (num6 > num5 && num6 < end)
					{
						action();
						string label = text.Substring(num + 1, num5 - num - 1);
						string rawUrl = text.Substring(num5 + 2, num6 - num5 - 2).Trim();
						output.Append(Hyperlink(label, rawUrl, style, context));
						num = num6 + 1;
						continue;
					}
				}
			}
			if (num + 1 < end && text[num] == '!' && text[num + 1] == '[')
			{
				int num7 = text.IndexOf(']', num + 2);
				int num8 = ((num7 >= 0 && num7 + 1 < end && text[num7 + 1] == '(') ? text.IndexOf(')', num7 + 2) : (-1));
				if (num7 > num && num8 > num7 && num8 < end)
				{
					plain.Append("[图片：").Append(text.Substring(num + 2, num7 - num - 2)).Append("]");
					num = num8 + 1;
					continue;
				}
			}
			if (text[num] != '<' || text.IndexOf('>', num + 1) < 0)
			{
				plain.Append(text[num]);
			}
			else
			{
				num = text.IndexOf('>', num + 1);
			}
			num++;
		}
		action();
	}

	private static string Hyperlink(string label, string rawUrl, InlineStyle style, ExportContext context)
	{
		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var result) || (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
		{
			return Run(label, style);
		}
		string text = context.AddRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", result.ToString(), external: true);
		InlineStyle style2 = style.Clone();
		return "<w:hyperlink r:id=\"" + text + "\"><w:r><w:rPr>" + RunProperties(style2) + "<w:color w:val=\"3978B8\"/><w:u w:val=\"single\"/></w:rPr><w:t xml:space=\"preserve\">" + Escape(label) + "</w:t></w:r></w:hyperlink>";
	}

	private static string Run(string text, InlineStyle style)
	{
		return "<w:r><w:rPr>" + RunProperties(style) + "</w:rPr><w:t xml:space=\"preserve\">" + Escape(text) + "</w:t></w:r>";
	}

	private static string RunProperties(InlineStyle style)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (style.Bold)
		{
			stringBuilder.Append("<w:b/>");
		}
		if (style.Italic)
		{
			stringBuilder.Append("<w:i/>");
		}
		if (style.Strike)
		{
			stringBuilder.Append("<w:strike/>");
		}
		if (style.Code)
		{
			stringBuilder.Append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:eastAsia=\"Microsoft YaHei UI\"/><w:sz w:val=\"19\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F2F4F7\"/>");
		}
		return stringBuilder.ToString();
	}

	private static string? ImageParagraph(string rawSource, string alt, ExportContext context)
	{
		string text = rawSource.Trim().Trim('<', '>');
		if (Uri.TryCreate(text, UriKind.Absolute, out var result) && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps))
		{
			return Paragraph("[网络图片：" + (string.IsNullOrWhiteSpace(alt) ? text : alt) + "]", null, context);
		}
		if (Path.IsPathRooted(text))
		{
			return null;
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(Path.Combine(context.SourceDirectory, Uri.UnescapeDataString(text.Replace('/', Path.DirectorySeparatorChar))));
			string value = context.SourceDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
			{
				return null;
			}
		}
		catch
		{
			return null;
		}
		int num;
		int num2;
		byte[] bytes;
		string text2;
		string contentType;
		try
		{
			byte[] array = File.ReadAllBytes(fullPath);
			if (array.Length > 10485760)
			{
				return null;
			}
			using MemoryStream stream = new MemoryStream(array);
			using Image image = Image.FromStream(stream);
			num = Math.Max(1, image.Width);
			num2 = Math.Max(1, image.Height);
			switch (Path.GetExtension(fullPath).ToLowerInvariant())
			{
			case ".png":
				bytes = array;
				text2 = "png";
				contentType = "image/png";
				break;
			case ".jpg":
			case ".jpeg":
				bytes = array;
				text2 = "jpeg";
				contentType = "image/jpeg";
				break;
			default:
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					image.Save(memoryStream, ImageFormat.Png);
					bytes = memoryStream.ToArray();
				}
				text2 = "png";
				contentType = "image/png";
				break;
			}
			}
		}
		catch
		{
			return null;
		}
		string text3 = "image" + (context.Media.Count + 1) + "." + text2;
		context.Media.Add(new MediaPart
		{
			Name = text3,
			ContentType = contentType,
			Bytes = bytes
		});
		string text4 = context.AddRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/" + text3);
		long num3 = (long)num * 9525L;
		long num4 = (long)num2 * 9525L;
		double num5 = Math.Min(1.0, Math.Min(5760720.0 / (double)num3, 7315200.0 / (double)num4));
		num3 = Math.Max(1L, (long)((double)num3 * num5));
		num4 = Math.Max(1L, (long)((double)num4 * num5));
		int num6 = context.NextDrawing++;
		string text5 = Escape(string.IsNullOrWhiteSpace(alt) ? Path.GetFileName(fullPath) : alt);
		return "<w:p><w:pPr><w:spacing w:before=\"100\" w:after=\"140\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"" + num3 + "\" cy=\"" + num4 + "\"/><wp:docPr id=\"" + num6 + "\" name=\"" + text5 + "\" descr=\"" + text5 + "\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"" + text5 + "\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"" + text4 + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + num3 + "\" cy=\"" + num4 + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
	}

	private static string StripInlineMarkers(string text)
	{
		return Regex.Replace(Regex.Replace(text, "(!?\\[)([^\\]]+)(\\]\\([^\\)]+\\))", "$2"), "(\\*\\*|__|~~|`|\\*)", "");
	}

	private static string SectionProperties()
	{
		return "<w:sectPr><w:pgSz w:w=\"" + 12240 + "\" w:h=\"" + 15840 + "\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/><w:cols w:space=\"708\"/><w:docGrid w:linePitch=\"312\"/></w:sectPr>";
	}

	private static string Styles()
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Microsoft YaHei UI\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"zh-CN\" w:eastAsia=\"zh-CN\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"290\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>\n<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/><w:pPr><w:spacing w:after=\"120\" w:line=\"290\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Microsoft YaHei UI\"/><w:sz w:val=\"22\"/></w:rPr></w:style>\n<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"360\" w:after=\"200\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2E74B5\"/><w:sz w:val=\"32\"/></w:rPr></w:style>\n<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"280\" w:after=\"140\"/><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2E74B5\"/><w:sz w:val=\"26\"/></w:rPr></w:style>\n<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"200\" w:after=\"100\"/><w:outlineLvl w:val=\"2\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"1F4D78\"/><w:sz w:val=\"24\"/></w:rPr></w:style>\n<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"360\"/><w:pBdr><w:left w:val=\"single\" w:sz=\"18\" w:space=\"8\" w:color=\"89A9D8\"/></w:pBdr><w:spacing w:before=\"100\" w:after=\"140\"/></w:pPr><w:rPr><w:i/><w:color w:val=\"52606D\"/></w:rPr></w:style>\n</w:styles>";
	}

	private static string Numbering()
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" + NumberLevels("bullet") + "</w:abstractNum>\n<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"multilevel\"/>" + NumberLevels("decimal") + "</w:abstractNum>\n<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>";
	}

	private static string NumberLevels(string format)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < 3; i++)
		{
			int value = 540 + i * 540;
			string value2 = ((!(format == "bullet")) ? ("%" + (i + 1) + ".") : ((i == 1) ? "o" : "•"));
			stringBuilder.Append("<w:lvl w:ilvl=\"").Append(i).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"")
				.Append(format)
				.Append("\"/><w:lvlText w:val=\"")
				.Append(Escape(value2))
				.Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:tabs><w:tab w:val=\"num\" w:pos=\"")
				.Append(value)
				.Append("\"/></w:tabs><w:ind w:left=\"")
				.Append(value)
				.Append("\" w:hanging=\"270\"/><w:spacing w:after=\"80\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr><w:rPr>");
			if (format == "bullet")
			{
				stringBuilder.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:hint=\"default\"/>");
			}
			stringBuilder.Append("</w:rPr></w:lvl>");
		}
		return stringBuilder.ToString();
	}

	private static string ContentTypes(ExportContext context)
	{
		StringBuilder stringBuilder = new StringBuilder("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
		foreach (MediaPart item in from item in context.Media
			group item by Path.GetExtension(item.Name).TrimStart('.').ToLowerInvariant() into @group
			select @group.First())
		{
			stringBuilder.Append("<Default Extension=\"").Append(Path.GetExtension(item.Name).TrimStart('.')).Append("\" ContentType=\"")
				.Append(item.ContentType)
				.Append("\"/>");
		}
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" + stringBuilder?.ToString() + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
	}

	private static string PackageRelationships()
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";
	}

	private static string DocumentRelationships(ExportContext context)
	{
		StringBuilder stringBuilder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>").Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"styles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/><Relationship Id=\"numbering\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>");
		foreach (Relationship relationship in context.Relationships)
		{
			stringBuilder.Append("<Relationship Id=\"").Append(relationship.Id).Append("\" Type=\"")
				.Append(Escape(relationship.Type))
				.Append("\" Target=\"")
				.Append(Escape(relationship.Target))
				.Append("\"")
				.Append(relationship.External ? " TargetMode=\"External\"" : "")
				.Append("/>");
		}
		return stringBuilder.Append("</Relationships>").ToString();
	}

	private static string CoreProperties(string title)
	{
		string text = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:title>" + Escape(title) + "</dc:title><dc:creator>MD Viewer</dc:creator><cp:lastModifiedBy>MD Viewer</cp:lastModifiedBy><dcterms:created xsi:type=\"dcterms:W3CDTF\">" + text + "</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + text + "</dcterms:modified></cp:coreProperties>";
	}

	private static string AppProperties()
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>MD Viewer</Application><AppVersion>2.2.0</AppVersion></Properties>";
	}

	private static void WriteText(ZipArchive archive, string path, string content)
	{
		using Stream stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
		using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.Write(content);
	}

	private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
	{
		using Stream stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
		stream.Write(bytes, 0, bytes.Length);
	}

	private static string Escape(string value)
	{
		return SecurityElement.Escape(value ?? "") ?? "";
	}
}
