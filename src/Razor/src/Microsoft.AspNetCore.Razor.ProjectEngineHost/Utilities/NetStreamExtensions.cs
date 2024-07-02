// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

// These extensions are only used in NET5+ and are using APIs not available in NetFX
#if NET

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.ProjectSystem;

namespace Microsoft.AspNetCore.Razor.Utilities;

internal static class NetStreamExtensions
{
    public static Task WriteStringAsync(this Stream stream, string text, Encoding? encoding, CancellationToken cancellationToken)
    {
        Debug.Assert(stream.CanWrite);
        encoding ??= Encoding.UTF8;

        var byteCount = encoding.GetMaxByteCount(text.Length);
        using var _ = ArrayPool<byte>.Shared.GetPooledArray(byteCount, out var byteArray);

        var usedBytes = encoding.GetBytes(text, byteArray);

        WriteSize(stream, usedBytes);
        return stream.WriteAsync(byteArray, 0, usedBytes, cancellationToken);
    }

    public static async Task<string> ReadStringAsync(this Stream stream, Encoding? encoding, CancellationToken cancellationToken)
    {
        Debug.Assert(stream.CanRead);
        encoding ??= Encoding.UTF8;

        var length = ReadSize(stream);

        using var _ = ArrayPool<byte>.Shared.GetPooledArray(length, out var encodedBytes);
        await stream.ReadAsync(encodedBytes, 0, length, cancellationToken).ConfigureAwait(false);
        return encoding.GetString(encodedBytes, 0, length);
    }

    public static ProjectInfoAction ReadProjectInfoAction(this Stream stream)
    {
        var action = stream.ReadByte();
        return action switch
        {
            0 => ProjectInfoAction.Update,
            1 => ProjectInfoAction.Remove,
            _ => throw Assumes.NotReachable()
        };
    }

    public static void WriteProjectInfoAction(this Stream stream, ProjectInfoAction projectInfoAction)
    {
        stream.WriteByte(projectInfoAction switch
        {
            ProjectInfoAction.Update => 0,
            ProjectInfoAction.Remove => 1,
            _ => throw Assumes.NotReachable()
        });
    }

    public static Task WriteProjectInfoRemovalAsync(this Stream stream, string intermediateOutputPath, CancellationToken cancellationToken)
    {
        WriteProjectInfoAction(stream, ProjectInfoAction.Remove);
        return stream.WriteStringAsync(intermediateOutputPath, encoding: null, cancellationToken);
    }

    public static Task<string> ReadProjectInfoRemovalAsync(this Stream stream, CancellationToken cancellationToken)
    {
        return stream.ReadStringAsync(encoding: null, cancellationToken);
    }

    public static async Task WriteProjectInfoAsync(this Stream stream, RazorProjectInfo projectInfo, CancellationToken cancellationToken)
    {
        WriteProjectInfoAction(stream, ProjectInfoAction.Update);

        var bytes = projectInfo.Serialize();
        WriteSize(stream, bytes.Length);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RazorProjectInfo?> ReadProjectInfoAsync(this Stream stream, CancellationToken cancellationToken)
    {
        var sizeToRead = ReadSize(stream);

        using var _ = ArrayPool<byte>.Shared.GetPooledArray(sizeToRead, out var projectInfoBytes);
        await stream.ReadAsync(projectInfoBytes, 0, projectInfoBytes.Length, cancellationToken).ConfigureAwait(false);
        return RazorProjectInfo.DeserializeFrom(projectInfoBytes.AsMemory());
    }

    private static void WriteSize(Stream stream, int length)
    {
        Span<byte> sizeBytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(sizeBytes, length);
        stream.Write(sizeBytes);
    }

    private unsafe static int ReadSize(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.Read(bytes);
        return BitConverter.ToInt32(bytes);
    }
}
#endif
