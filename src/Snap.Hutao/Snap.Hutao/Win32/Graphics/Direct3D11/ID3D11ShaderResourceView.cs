// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snap.Hutao.Win32.Graphics.Direct3D11;

[SupportedOSPlatform("windows6.1")]
internal static unsafe class ID3D11ShaderResourceView
{
    internal static ref readonly Guid IID
    {
        get => ref MemoryMarshal.AsRef<Guid>([0xE0, 0x6F, 0xE0, 0xB0, 0x92, 0x81, 0x1A, 0x4E, 0xB1, 0xCA, 0x36, 0xD7, 0x41, 0x47, 0x10, 0xB2]);
    }

    internal readonly struct Vftbl
    {
        internal readonly ID3D11View.Vftbl ID3D11ViewVftbl;
        internal readonly delegate* unmanaged[Stdcall]<nint, D3D11_SHADER_RESOURCE_VIEW_DESC*, void> GetDesc;
    }
}