using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace TenantPlatform.Infrastructure.Agreements;

public class LocalAgreementDocumentStorage(IOptions<AgreementDocumentStorageOptions> options) : IAgreementDocumentStorage
{
    private readonly string root = Path.GetFullPath(options.Value.RootPath);
    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;

    public async Task<StoredAgreementFile> StoreAsync(Stream source, string originalFileName, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(originalFileName.Replace('\\', '/'));
        fileName = new string(fileName.Where(c => !char.IsControl(c) && c is not '"' and not ':' and not '<' and not '>' and not '|').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200)
            throw new AgreementFileException("AgreementInvalidFileName");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx" or ".xlsx" or ".png" or ".jpg" or ".jpeg"))
            throw new AgreementFileException("AgreementInvalidFileType");
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        var destination = GetPath(key);
        var temporary = destination + ".partial";
        try
        {
            long size = 0;
            string mediaType;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    size += read;
                    if (size > MaxFileSizeBytes) throw new AgreementFileException("AgreementFileTooLarge");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (size == 0) throw new AgreementFileException("AgreementInvalidFileType");
                await file.FlushAsync(cancellationToken);
                file.Position = 0;
                mediaType = ValidateContent(file, extension);
                cancellationToken.ThrowIfCancellationRequested();
            }
            File.Move(temporary, destination, overwrite: false);
            return new StoredAgreementFile(key, fileName, mediaType, size);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(GetPath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }

    // Only for failed uploads whose metadata was not committed; no public delete operation.
    public Task DiscardUncommittedAsync(string storageKey)
    {
        File.Delete(GetPath(storageKey));
        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(root, key);
    }

    private static string ValidateContent(Stream file, string extension)
    {
        Span<byte> header = stackalloc byte[8];
        var count = file.Read(header);
        file.Position = 0;
        if (extension == ".pdf" && count >= 5 && header[..5].SequenceEqual("%PDF-"u8))
        {
            file.Position = Math.Max(0, file.Length - 1024);
            using var reader = new StreamReader(file, Encoding.ASCII, false, 1024, leaveOpen: true);
            if (reader.ReadToEnd().Contains("%%EOF", StringComparison.Ordinal)) return "application/pdf";
        }
        if (extension == ".png" && count == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && file.Length >= 33)
            return "image/png";
        if (extension is ".jpg" or ".jpeg" && count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255)
        {
            file.Position = file.Length - 2;
            if (file.ReadByte() == 255 && file.ReadByte() == 217) return "image/jpeg";
        }
        if (extension is ".docx" or ".xlsx" && count >= 4 && header[..4].SequenceEqual("PK\x03\x04"u8))
        {
            try
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
                if (zip.Entries.Count > 10000 || zip.Entries.Sum(x => x.Length) > 200L * 1024 * 1024 ||
                    zip.Entries.Any(x => x.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                    throw new AgreementFileException("AgreementInvalidFileType");
                var main = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
                var mainType = extension == ".docx"
                    ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                    : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
                var types = zip.GetEntry("[Content_Types].xml");
                if (zip.GetEntry(main) is not { Length: > 0 } || types is null || types.Length > 65536)
                    throw new AgreementFileException("AgreementInvalidFileType");
                using var content = types.Open();
                using var xml = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
                var document = XDocument.Load(xml);
                XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
                if (document.Root?.Elements(ns + "Override").Any(x =>
                    (string?)x.Attribute("PartName") == "/" + main && (string?)x.Attribute("ContentType") == mainType) == true)
                    return extension == ".docx"
                        ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            }
            catch (InvalidDataException) { throw new AgreementFileException("AgreementInvalidFileType"); }
            catch (XmlException) { throw new AgreementFileException("AgreementInvalidFileType"); }
        }
        throw new AgreementFileException("AgreementInvalidFileType");
    }
}
