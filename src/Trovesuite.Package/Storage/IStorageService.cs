using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Storage;

public interface IStorageService
{
    Task<Respons<StorageContainerCreateServiceReadDto>> CreateContainerAsync(StorageContainerCreateServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileUploadServiceReadDto>> UploadFileAsync(StorageFileUploadServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileUpdateServiceReadDto>> UpdateFileAsync(StorageFileUpdateServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileDeleteServiceReadDto>> DeleteFileAsync(StorageFileDeleteServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileDeleteServiceReadDto>> DeleteMultipleFilesAsync(string storageAccountUrl, string containerName, IEnumerable<string> blobNames, string? managedIdentityClientId = null, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileDownloadServiceReadDto>> DownloadFileAsync(StorageFileDownloadServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<StorageFileUrlServiceReadDto>> GetFileUrlAsync(StorageFileUrlServiceWriteDto data, CancellationToken cancellationToken = default);
}
