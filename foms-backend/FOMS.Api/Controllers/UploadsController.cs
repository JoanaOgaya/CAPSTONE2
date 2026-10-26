using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FOMS.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/uploads")]
public class UploadsController : ControllerBase
{
    private static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".csv", ".xlsx" };
    private static readonly string[] AllowedMimeTypes =
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB limit (NFR-06)

    private readonly string _uploadBasePath;

    public UploadsController()
    {
        _uploadBasePath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProtectedUploads");
        if (!Directory.Exists(_uploadBasePath))
        {
            Directory.CreateDirectory(_uploadBasePath);
        }
    }

    private (string role, string? clientId) GetCaller()
    {
        if (User == null) return (string.Empty, null);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var clientId = User.FindFirst("client_id")?.Value;
        return (role, clientId);
    }

    private static bool ValidateFileContent(Stream stream, string extension)
    {
        var buffer = new byte[512];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        stream.Position = 0; // Rewind for file save

        if (bytesRead < 4 && extension != ".csv") return false;

        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                // Positive signature: %PDF (0x25, 0x50, 0x44, 0x46)
                return bytesRead >= 4 &&
                       buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46;

            case ".jpg":
            case ".jpeg":
                // Positive signature: FFD8FF (0xFF, 0xD8, 0xFF)
                return bytesRead >= 3 &&
                       buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;

            case ".png":
                // Positive signature: 89 50 4E 47 0D 0A 1A 0A
                return bytesRead >= 8 &&
                       buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
                       buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A;

            case ".xlsx":
                // Positive signature: ZIP header PK\x03\x04 or PK\x05\x06 (0x50, 0x4B)
                return bytesRead >= 4 &&
                       buffer[0] == 0x50 && buffer[1] == 0x4B &&
                       ((buffer[2] == 0x03 && buffer[3] == 0x04) || (buffer[2] == 0x05 && buffer[3] == 0x06));

            case ".csv":
                // Text/UTF-8 check: no binary null bytes (0x00) and decodable UTF-8 string
                if (bytesRead == 0) return true; // Empty CSV is valid empty text
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0x00) return false;
                }
                try
                {
                    _ = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    return true;
                }
                catch
                {
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// POST /api/uploads — Upload a validated, protected document (NFR-06).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        // 1. Size Validation (Max 10MB)
        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = $"File size ({file.Length / (1024 * 1024.0):F2}MB) exceeds the maximum allowed limit of 10MB." });
        }

        // 2. Extension Validation (Whitelist only)
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            return BadRequest(new { message = $"File extension '{ext}' is not permitted. Allowed extensions: {string.Join(", ", AllowedExtensions)}." });
        }

        // 3. MIME Type Validation
        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()) && file.ContentType != "application/octet-stream")
        {
            return BadRequest(new { message = $"Content-Type '{file.ContentType}' is not permitted." });
        }

        // 4. Content / Magic-Byte Validation (detect renamed executables)
        using (var stream = file.OpenReadStream())
        {
            if (!ValidateFileContent(stream, ext))
            {
                return BadRequest(new { message = $"Content validation failed: The file contents do not match the expected signature for '{ext}'." });
            }
        }

        // 5. Tenant tagging & randomized safe filename
        var (role, clientIdClaim) = GetCaller();
        var tenantTag = !string.IsNullOrWhiteSpace(clientIdClaim) ? $"TENANT_{clientIdClaim}" : "INTERNAL";
        var cleanFileName = Path.GetFileName(file.FileName).Replace(" ", "_");
        var safeFileName = $"{tenantTag}_{Guid.NewGuid():N}_{cleanFileName}";
        var destinationPath = Path.Combine(_uploadBasePath, safeFileName);

        using (var stream = new FileStream(destinationPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var protectedUrl = $"/api/uploads/protected/{safeFileName}";

        return Ok(new
        {
            success = true,
            message = "File uploaded successfully.",
            url = protectedUrl,
            fileName = file.FileName,
            size = file.Length,
            contentType = file.ContentType
        });
    }

    /// <summary>
    /// GET /api/uploads/protected/{fileName} — Stream protected file to authenticated users only.
    /// Enforces path-traversal protection and tenant isolation (IAM-06, NFR-06).
    /// </summary>
    [HttpGet("protected/{fileName}")]
    public IActionResult GetProtectedFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
        {
            return BadRequest(new { message = "Path traversal or invalid file path detected." });
        }

        var safeName = Path.GetFileName(fileName);
        var filePath = Path.Combine(_uploadBasePath, safeName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Requested file not found." });
        }

        // IAM-06 / NFR-06: Client tenant download isolation
        var (role, clientIdClaim) = GetCaller();
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim))
            {
                return Forbid();
            }

            if (safeName.StartsWith("TENANT_", StringComparison.OrdinalIgnoreCase))
            {
                var expectedPrefix = $"TENANT_{clientIdClaim}_";
                if (!safeName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

        return PhysicalFile(filePath, mime);
    }
}
