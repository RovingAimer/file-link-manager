using System.Runtime.InteropServices;
using FileLinkManager.Infrastructure.Native;

namespace FileLinkManager.Infrastructure.Tests.Native;

public sealed class FileDispositionInfoTests
{
    [Fact]
    public void DeleteFileBoolean_UsesOneByteLayoutAndTrueValue()
    {
        var disposition = new FileDispositionInfo { DeleteFile = 1 };

        Assert.Equal(1, Marshal.SizeOf<FileDispositionInfo>());

        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.StructureToPtr(disposition, buffer, fDeleteOld: false);

            Assert.Equal((byte)1, Marshal.ReadByte(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
