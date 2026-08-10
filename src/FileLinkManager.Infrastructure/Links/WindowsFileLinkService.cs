using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Errors;
using FileLinkManager.Domain.Links;
using FileLinkManager.Infrastructure.Native;
using FileLinkManager.Infrastructure.Paths;

namespace FileLinkManager.Infrastructure.Links;

public sealed partial class WindowsFileLinkService : IFileLinkService
{
    private readonly WindowsFileIdentityReader identityReader = new();
    private readonly ReparsePointReader reparsePointReader = new();
    private readonly WindowsPathService pathService;
    private readonly Action<string, byte[]>? setMountPointReparseData;
    private readonly Action? beforeJunctionDisposition;
    private readonly Action? beforeHardLinkDisposition;

    public WindowsFileLinkService()
        : this(
            new WindowsPathService(),
            setMountPointReparseData: null,
            beforeJunctionDisposition: null,
            beforeHardLinkDisposition: null)
    {
    }

    public WindowsFileLinkService(WindowsPathService pathService)
        : this(
            pathService,
            setMountPointReparseData: null,
            beforeJunctionDisposition: null,
            beforeHardLinkDisposition: null)
    {
    }

    internal WindowsFileLinkService(WindowsPathService pathService, Action<string, byte[]> setMountPointReparseData)
        : this(
            pathService,
            setMountPointReparseData,
            beforeJunctionDisposition: null,
            beforeHardLinkDisposition: null)
    {
    }

    internal WindowsFileLinkService(
        WindowsPathService pathService,
        Action<string, byte[]>? setMountPointReparseData,
        Action? beforeJunctionDisposition,
        Action? beforeHardLinkDisposition = null)
    {
        this.pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        this.setMountPointReparseData = setMountPointReparseData;
        this.beforeJunctionDisposition = beforeJunctionDisposition;
        this.beforeHardLinkDisposition = beforeHardLinkDisposition;
    }

    public LinkValidationResult Validate(LinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateValidationResult(request.Type, pathService.Validate(request).Errors.Select(error => error.Message));
    }

    public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var pathValidation = pathService.Validate(request);
        var validation = CreateValidationResult(
            request.Type,
            pathValidation.Errors.Select(error => error.Message));
        if (!validation.IsValid)
        {
            var errorPath = IsSymbolicLink(request.Type)
                ? pathValidation.LinkPath?.DisplayPath ?? request.LinkPath
                : request.LinkPath;
            throw new FileLinkException(GetCreateOperation(request.Type), errorPath, string.Join(" ", validation.Errors));
        }

        var normalizedSourcePath = pathValidation.SourcePath!.DisplayPath;
        var normalizedSourceComparisonPath = pathValidation.SourcePath.ComparisonPath;
        var normalizedLinkPath = pathValidation.LinkPath!.DisplayPath;

        if (request.Type == FileLinkType.FileHardLink)
        {
            return CreateHardLinkAsync(
                normalizedSourcePath,
                normalizedSourceComparisonPath,
                normalizedLinkPath,
                cancellationToken);
        }

        if (request.Type == FileLinkType.DirectoryJunction)
        {
            return CreateDirectoryJunctionAsync(
                normalizedSourcePath,
                normalizedSourceComparisonPath,
                normalizedLinkPath);
        }

        return CreateSymbolicLinkAsync(
            request.Type,
            normalizedSourcePath,
            normalizedSourceComparisonPath,
            normalizedLinkPath);
    }

    public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileAttributes? attributes;
        try
        {
            attributes = GetPathAttributes(linkPath);
        }
        catch (FileLinkException exception)
        {
            return Task.FromResult(new LinkInspection(linkPath, null, LinkStatus.Unknown, null, exception.Message));
        }

        if (!attributes.HasValue)
        {
            return Task.FromResult(new LinkInspection(linkPath, null, LinkStatus.Missing, null, null));
        }

        if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            try
            {
                var reparsePoint = reparsePointReader.Read(linkPath);
                if (!IsSupportedReparseLink(reparsePoint.FileLinkType))
                {
                    return Task.FromResult(CreateReparseLinkInspection(
                        linkPath,
                        reparsePoint,
                        LinkStatus.Conflict,
                        $"Link type '{reparsePoint.FileLinkType}' is not supported by this service."));
                }

                return Task.FromResult(CreateReparseLinkInspection(linkPath, reparsePoint, LinkStatus.Valid, null));
            }
            catch (FileLinkException exception)
            {
                return Task.FromResult(new LinkInspection(linkPath, null, LinkStatus.Unknown, null, exception.Message));
            }
        }

        if ((attributes.Value & FileAttributes.Directory) != 0)
        {
            return Task.FromResult(new LinkInspection(
                linkPath,
                null,
                LinkStatus.Conflict,
                null,
                "The link path is a directory, but this service supports file and directory symbolic links plus directory junctions."));
        }

        try
        {
            var fingerprint = identityReader.Read(linkPath);
            return Task.FromResult(new LinkInspection(linkPath, FileLinkType.FileHardLink, LinkStatus.Valid, fingerprint, null));
        }
        catch (FileLinkException exception)
        {
            return Task.FromResult(new LinkInspection(linkPath, null, LinkStatus.Unknown, null, exception.Message));
        }
    }

    public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var request = record.Request;
        if (request.Type == FileLinkType.FileSymbolicLink)
        {
            return DeleteFileSymbolicLinkAsync(record);
        }

        if (request.Type == FileLinkType.DirectorySymbolicLink)
        {
            return DeleteDirectorySymbolicLinkAsync(record);
        }

        if (request.Type == FileLinkType.DirectoryJunction)
        {
            return DeleteDirectoryJunctionAsync(record);
        }

        if (request.Type != FileLinkType.FileHardLink)
        {
            throw new FileLinkException(
                "DeleteFileW",
                request.LinkPath,
                $"Link type '{request.Type}' is not supported by this service.");
        }

        return DeleteHardLinkAsync(record, cancellationToken);
    }

    private static LinkInspection CreateReparseLinkInspection(
        string linkPath,
        ReparsePointInfo reparsePoint,
        LinkStatus status,
        string? error,
        string? normalizedLinkPath = null) =>
        new(
            linkPath,
            reparsePoint.FileLinkType,
            status,
            new LinkFingerprint(reparsePoint.Tag, reparsePoint.TargetPath, null, null, normalizedLinkPath),
            error);

    private static string GetCreateOperation(FileLinkType type) =>
        type == FileLinkType.DirectoryJunction
            ? "FSCTL_SET_REPARSE_POINT"
            : IsSymbolicLink(type) ? "CreateSymbolicLinkW" : "CreateHardLinkW";

    private static LinkValidationResult CreateValidationResult(
        FileLinkType type,
        IEnumerable<string> pathErrors)
    {
        var errors = pathErrors.ToList();
        if (type is not FileLinkType.FileHardLink
            and not FileLinkType.FileSymbolicLink
            and not FileLinkType.DirectorySymbolicLink
            and not FileLinkType.DirectoryJunction)
        {
            errors.Add($"Link type '{type}' is not supported yet.");
        }

        return errors.Count == 0
            ? LinkValidationResult.Success
            : new LinkValidationResult(false, errors);
    }

    private static bool IsSymbolicLink(FileLinkType type) =>
        type is FileLinkType.FileSymbolicLink or FileLinkType.DirectorySymbolicLink;

    private static bool IsSupportedReparseLink(FileLinkType type) =>
        IsSymbolicLink(type) || type == FileLinkType.DirectoryJunction;

    private static FileAttributes? GetPathAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileLinkException(
                "GetFileAttributesW",
                path,
                $"Conflict: unable to safely determine the link path type. {exception.Message}");
        }
    }
}
