using System.Text.Json.Serialization;

namespace Trovesuite.Package.Storage;

public class StorageContainerCreateServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("public_access")]
    public string? PublicAccess { get; set; }

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageContainerCreateServiceReadDto
{
    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("container_url")]
    public string? ContainerUrl { get; set; }
}

public class StorageFileUploadServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("file_content")]
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("directory_path")]
    public string? DirectoryPath { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageFileUploadServiceReadDto
{
    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("blob_url")]
    public string BlobUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public class StorageFileUpdateServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("file_content")]
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageFileUpdateServiceReadDto
{
    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("blob_url")]
    public string BlobUrl { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public class StorageFileDeleteServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageFileDeleteServiceReadDto
{
    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
}

public class StorageFileDownloadServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageFileDownloadServiceReadDto
{
    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public byte[] Content { get; set; } = Array.Empty<byte>();

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public class StorageFileUrlServiceWriteDto
{
    [JsonPropertyName("storage_account_url")]
    public string StorageAccountUrl { get; set; } = string.Empty;

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = string.Empty;

    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("expiry_hours")]
    public int? ExpiryHours { get; set; } = 1;

    [JsonPropertyName("managed_identity_client_id")]
    public string? ManagedIdentityClientId { get; set; }
}

public class StorageFileUrlServiceReadDto
{
    [JsonPropertyName("blob_name")]
    public string BlobName { get; set; } = string.Empty;

    [JsonPropertyName("presigned_url")]
    public string PresignedUrl { get; set; } = string.Empty;

    [JsonPropertyName("expires_in_hours")]
    public int ExpiresInHours { get; set; }
}
