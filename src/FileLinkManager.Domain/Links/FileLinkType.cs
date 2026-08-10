namespace FileLinkManager.Domain.Links;

public enum FileLinkType
{
    FileSymbolicLink,
    DirectorySymbolicLink,
    FileHardLink,
    DirectoryJunction
}
