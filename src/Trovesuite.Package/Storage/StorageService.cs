using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Storage;

public sealed class StorageService : IStorageService
{
    private readonly ILogger<StorageService> _logger;

    public StorageService(ILogger<StorageService> logger)
    {
        _logger = logger;
    }

    private static TokenCredential GetCredential(string? managedIdentityClientId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
                return new ManagedIdentityCredential(managedIdentityClientId);

            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
            });
        }
        catch
        {
            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
            });
        }
    }

    private static BlobServiceClient GetBlobServiceClient(string storageAccountUrl, string? managedIdentityClientId)
    {
        // A silo with its own storage ACCOUNT is redirected here, the one place
        // every operation opens a client. A pooled tenant's URL is unchanged.
        return new BlobServiceClient(
            new Uri(Tenancy.TenantStorage.AccountUrl(storageAccountUrl)),
            GetCredential(managedIdentityClientId));
    }

    public async Task<Respons<StorageContainerCreateServiceReadDto>> CreateContainerAsync(StorageContainerCreateServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var access = string.IsNullOrWhiteSpace(data.PublicAccess)
                ? PublicAccessType.None
                : Enum.TryParse<PublicAccessType>(data.PublicAccess, true, out var pa) ? pa : PublicAccessType.None;

            var containerClient = await blobService.CreateBlobContainerAsync(Tenancy.TenantStorage.Container(data.ContainerName), access, cancellationToken: cancellationToken).ConfigureAwait(false);

            var result = new StorageContainerCreateServiceReadDto
            {
                ContainerName = Tenancy.TenantStorage.Container(data.ContainerName),
                ContainerUrl = containerClient.Value.Uri.ToString(),
            };
            return Respons<StorageContainerCreateServiceReadDto>.Ok(new[] { result }, $"Container '{data.ContainerName}' created successfully", 201);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return Respons<StorageContainerCreateServiceReadDto>.Fail("Resource already exists", $"Container '{data.ContainerName}' already exists", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create container {Container}", data.ContainerName);
            return Respons<StorageContainerCreateServiceReadDto>.Fail(ex.Message, "Failed to create container", 500);
        }
    }

    public async Task<Respons<StorageFileUploadServiceReadDto>> UploadFileAsync(StorageFileUploadServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        var blobName = data.BlobName;
        if (!string.IsNullOrWhiteSpace(data.DirectoryPath))
        {
            var dir = data.DirectoryPath.Trim('/');
            blobName = $"{dir}/{blobName}";
        }

        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var blob = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(data.ContainerName)).GetBlobClient(blobName);

            var headers = data.ContentType is null ? null : new BlobHttpHeaders { ContentType = data.ContentType };
            using var stream = new MemoryStream(data.FileContent);
            await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken).ConfigureAwait(false);

            var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = new StorageFileUploadServiceReadDto
            {
                BlobName = blobName,
                BlobUrl = blob.Uri.ToString(),
                ContentType = props.Value.ContentType,
                Size = props.Value.ContentLength,
            };
            return Respons<StorageFileUploadServiceReadDto>.Ok(new[] { result }, $"File '{blobName}' uploaded successfully", 201);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return Respons<StorageFileUploadServiceReadDto>.Fail("Resource already exists. Use update_file to modify existing files.", $"File '{blobName}' already exists", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {Blob}", blobName);
            return Respons<StorageFileUploadServiceReadDto>.Fail(ex.Message, "Failed to upload file", 500);
        }
    }

    public async Task<Respons<StorageFileUpdateServiceReadDto>> UpdateFileAsync(StorageFileUpdateServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var blob = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(data.ContainerName)).GetBlobClient(data.BlobName);

            if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
                return Respons<StorageFileUpdateServiceReadDto>.Fail("Resource not found. Use upload_file to create new files.", $"File '{data.BlobName}' not found", 404);

            var headers = data.ContentType is null ? null : new BlobHttpHeaders { ContentType = data.ContentType };
            using var stream = new MemoryStream(data.FileContent);
            await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken).ConfigureAwait(false);

            var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = new StorageFileUpdateServiceReadDto
            {
                BlobName = data.BlobName,
                BlobUrl = blob.Uri.ToString(),
                UpdatedAt = props.Value.LastModified.UtcDateTime.ToString("o"),
            };
            return Respons<StorageFileUpdateServiceReadDto>.Ok(new[] { result }, $"File '{data.BlobName}' updated successfully", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file {Blob}", data.BlobName);
            return Respons<StorageFileUpdateServiceReadDto>.Fail(ex.Message, "Failed to update file", 500);
        }
    }

    public async Task<Respons<StorageFileDeleteServiceReadDto>> DeleteFileAsync(StorageFileDeleteServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var blob = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(data.ContainerName)).GetBlobClient(data.BlobName);

            var deleted = await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!deleted.Value)
                return Respons<StorageFileDeleteServiceReadDto>.Fail("Resource not found", $"File '{data.BlobName}' not found", 404);

            var result = new StorageFileDeleteServiceReadDto { BlobName = data.BlobName, Deleted = true };
            return Respons<StorageFileDeleteServiceReadDto>.Ok(new[] { result }, $"File '{data.BlobName}' deleted successfully", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {Blob}", data.BlobName);
            return Respons<StorageFileDeleteServiceReadDto>.Fail(ex.Message, "Failed to delete file", 500);
        }
    }

    public async Task<Respons<StorageFileDeleteServiceReadDto>> DeleteMultipleFilesAsync(string storageAccountUrl, string containerName, IEnumerable<string> blobNames, string? managedIdentityClientId = null, CancellationToken cancellationToken = default)
    {
        var results = new List<StorageFileDeleteServiceReadDto>();
        var errors = new List<string>();

        try
        {
            var blobService = GetBlobServiceClient(storageAccountUrl, managedIdentityClientId);
            var container = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(containerName));

            foreach (var blobName in blobNames)
            {
                try
                {
                    var blob = container.GetBlobClient(blobName);
                    var deleted = await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (deleted.Value)
                        results.Add(new StorageFileDeleteServiceReadDto { BlobName = blobName, Deleted = true });
                    else
                        errors.Add($"File '{blobName}' not found");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to delete '{blobName}': {ex.Message}");
                }
            }

            if (errors.Count > 0 && results.Count == 0)
                return Respons<StorageFileDeleteServiceReadDto>.Fail(string.Join("; ", errors), "Failed to delete any files", 500);

            if (errors.Count > 0)
            {
                var resp = Respons<StorageFileDeleteServiceReadDto>.Ok(results, $"Deleted {results.Count} file(s) with {errors.Count} error(s)", 207);
                resp.Error = string.Join("; ", errors);
                return resp;
            }

            return Respons<StorageFileDeleteServiceReadDto>.Ok(results, $"Successfully deleted {results.Count} file(s)", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete multiple files in {Container}", containerName);
            return Respons<StorageFileDeleteServiceReadDto>.Fail(ex.Message, "Failed to delete files", 500);
        }
    }

    public async Task<Respons<StorageFileDownloadServiceReadDto>> DownloadFileAsync(StorageFileDownloadServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var blob = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(data.ContainerName)).GetBlobClient(data.BlobName);

            if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
                return Respons<StorageFileDownloadServiceReadDto>.Fail("Resource not found", $"File '{data.BlobName}' not found", 404);

            var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var content = download.Value.Content.ToArray();
            var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var result = new StorageFileDownloadServiceReadDto
            {
                BlobName = data.BlobName,
                Content = content,
                ContentType = props.Value.ContentType,
                Size = props.Value.ContentLength,
            };
            return Respons<StorageFileDownloadServiceReadDto>.Ok(new[] { result }, $"File '{data.BlobName}' downloaded successfully", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {Blob}", data.BlobName);
            return Respons<StorageFileDownloadServiceReadDto>.Fail(ex.Message, "Failed to download file", 500);
        }
    }

    public async Task<Respons<StorageFileUrlServiceReadDto>> GetFileUrlAsync(StorageFileUrlServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobService = GetBlobServiceClient(data.StorageAccountUrl, data.ManagedIdentityClientId);
            var blob = blobService.GetBlobContainerClient(Tenancy.TenantStorage.Container(data.ContainerName)).GetBlobClient(data.BlobName);

            if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
                return Respons<StorageFileUrlServiceReadDto>.Fail("Resource not found", $"File '{data.BlobName}' not found", 404);

            var expiryHours = data.ExpiryHours ?? 1;
            var startsOn = DateTimeOffset.UtcNow;
            var expiresOn = startsOn.AddHours(expiryHours);

            var userDelegationKey = await blobService.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken).ConfigureAwait(false);

            var sasBuilder = new BlobSasBuilder
            {
                // The real container, not the requested one: a SAS signed for a
                // name that does not exist is simply an invalid URL.
                BlobContainerName = Tenancy.TenantStorage.Container(data.ContainerName),
                BlobName = data.BlobName,
                Resource = "b",
                ExpiresOn = expiresOn,
                StartsOn = startsOn,
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, blobService.AccountName).ToString();
            var presignedUrl = $"{blob.Uri}?{sasToken}";

            var result = new StorageFileUrlServiceReadDto
            {
                BlobName = data.BlobName,
                PresignedUrl = presignedUrl,
                ExpiresInHours = expiryHours,
            };
            return Respons<StorageFileUrlServiceReadDto>.Ok(new[] { result }, $"Presigned URL generated for '{data.BlobName}'", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for {Blob}", data.BlobName);
            return Respons<StorageFileUrlServiceReadDto>.Fail(ex.Message, "Failed to generate presigned URL", 500);
        }
    }
}
