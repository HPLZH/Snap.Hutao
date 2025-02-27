// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Core;
using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Core.LifeCycle.InterProcess.Yae;
using Snap.Hutao.Factory.ContentDialog;
using Snap.Hutao.Model.InterChange.Achievement;
using Snap.Hutao.Model.InterChange.Inventory;
using Snap.Hutao.Service.Game;
using Snap.Hutao.Service.Game.Launching.Handler;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.Service.Yae.Achievement;
using Snap.Hutao.Service.Yae.PlayerStore;
using Snap.Hutao.Win32.Foundation;
using Snap.Hutao.Win32.UI.WindowsAndMessaging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static Snap.Hutao.Win32.ConstValues;
using static Snap.Hutao.Win32.Kernel32;
using static Snap.Hutao.Win32.Macros;
using static Snap.Hutao.Win32.User32;

namespace Snap.Hutao.Service.Yae;

[ConstructorGenerated]
[Injection(InjectAs.Singleton, typeof(IYaeService))]
internal sealed partial class YaeService : IYaeService
{
    private readonly IContentDialogFactory contentDialogFactory;
    private readonly IServiceProvider serviceProvider;
    private readonly IInfoBarService infoBarService;
    private readonly LaunchOptions launchOptions;
    private readonly ITaskContext taskContext;

    private readonly string dataFolderYaePath = Path.Combine(HutaoRuntime.DataFolder, "YaeLib.dll");

    public async ValueTask<UIAF?> GetAchievementAsync()
    {
        ContentDialog dialog = await contentDialogFactory
            .CreateForIndeterminateProgressAsync(SH.ServiceYaeWaitForGameResponseMessage)
            .ConfigureAwait(false);

        using (await contentDialogFactory.BlockAsync(dialog).ConfigureAwait(false))
        {
            await taskContext.SwitchToBackgroundAsync();
            if (!InitializeGameProcess(out Process? process))
            {
                return default;
            }

#pragma warning disable CA2007
            await using (YaeNamedPipeServer server = new(serviceProvider, process))
#pragma warning restore CA2007
            {
                UIAF? uiaf = default;
                ImmutableArray<YaeData> dataArray = await server.GetDataArrayAsync().ConfigureAwait(false);
                foreach (YaeData data in dataArray)
                {
                    using (data)
                    {
                        if (data.Kind is YaeDataKind.Achievement)
                        {
                            Debug.Assert(uiaf is null);
                            uiaf = AchievementParser.Parse(data.Bytes);
                        }
                    }
                }

                return uiaf;
            }
        }
    }

    public async ValueTask<UIIF?> GetInventoryAsync()
    {
        ContentDialog dialog = await contentDialogFactory
            .CreateForIndeterminateProgressAsync(SH.ServiceYaeWaitForGameResponseMessage)
            .ConfigureAwait(false);

        using (await contentDialogFactory.BlockAsync(dialog).ConfigureAwait(false))
        {
            await taskContext.SwitchToBackgroundAsync();
            if (!InitializeGameProcess(out Process? process))
            {
                return default;
            }

#pragma warning disable CA2007
            await using (YaeNamedPipeServer server = new(serviceProvider, process))
#pragma warning restore CA2007
            {
                UIIF? uiif = default;
                Dictionary<InterestedPropType, double> propMap = [];
                ImmutableArray<YaeData> dataArray = await server.GetDataArrayAsync().ConfigureAwait(false);
                foreach (YaeData data in dataArray)
                {
                    using (data)
                    {
                        if (data.Kind is YaeDataKind.PlayerStore)
                        {
                            Debug.Assert(uiif is null);
                            uiif = PlayerStoreParser.Parse(data.Bytes);
                        }
                        else if (data.Kind is YaeDataKind.VirtualItem)
                        {
                            ref readonly YaePropertyTypeValue typeValue = ref data.PropertyTypeValue;
                            propMap.Add(typeValue.Type, typeValue.Value);
                        }
                    }
                }

                if (uiif is null)
                {
                    return default;
                }

                double count = propMap.GetValueOrDefault(InterestedPropType.PlayerSCoin) - propMap.GetValueOrDefault(InterestedPropType.PlayerWaitSubSCoin);
                UIIFItem mora = UIIFItem.From(202U, (uint)Math.Clamp(count, uint.MinValue, uint.MaxValue));

                return new()
                {
                    Info = uiif.Info,
                    List = [mora, .. uiif.List],
                };
            }
        }
    }

    private bool InitializeGameProcess([NotNullWhen(true)] out Process? process)
    {
        if (!launchOptions.TryGetGameFileSystem(out IGameFileSystem? gameFileSystem))
        {
            infoBarService.Warning(SH.ViewModelLaunchGameGamePathNullOrEmpty, SH.ServiceYaeGamePathNullOrEmptyMessage);
            process = default;
            return false;
        }

        if (LaunchExecutionEnsureGameNotRunningHandler.IsGameRunning())
        {
            infoBarService.Warning(SH.ServiceGameLaunchExecutionGameIsRunning, SH.ServiceYaeGameRunningMessage);
            process = default;
            return false;
        }

        process = new()
        {
            StartInfo = new()
            {
                FileName = gameFileSystem.GameFilePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = gameFileSystem.GetGameDirectory(),
            },
        };

        process.Start();
        InitializeYae(process);
        return true;
    }

    private unsafe void InitializeYae(Process gameProcess)
    {
        InstalledLocation.CopyFileFromApplicationUri("ms-appx:///YaeLib.dll", dataFolderYaePath);

        HANDLE hModule = default;
        try
        {
            hModule = NativeLibrary.Load(dataFolderYaePath);
            nint pYaeGetWindowHook = NativeLibrary.GetExport((nint)(hModule & ~0x3L), "YaeGetWindowHook");

            HOOKPROC hookProc = default;
            ((delegate* unmanaged[Stdcall]<HOOKPROC*, HRESULT>)pYaeGetWindowHook)(&hookProc);

            SpinWait.SpinUntil(() => gameProcess.MainWindowHandle is not 0);
            uint threadId = GetWindowThreadProcessId(gameProcess.MainWindowHandle, default);
            HHOOK hHook = SetWindowsHookExW(WINDOWS_HOOK_ID.WH_GETMESSAGE, hookProc, hModule, threadId);
            if (hHook.Value is 0)
            {
                Marshal.ThrowExceptionForHR(HRESULT_FROM_WIN32(GetLastError()));
                HutaoException.Throw("SetWindowsHookExW returned 'NULL' but no Error is presented");
            }

            if (!PostThreadMessageW(threadId, WM_NULL, default, default))
            {
                Marshal.ThrowExceptionForHR(HRESULT_FROM_WIN32(GetLastError()));
                HutaoException.Throw("PostThreadMessageW returned 'FALSE' but no Error is presented");
            }
        }
        finally
        {
            // NEVER UNHOOK: Will cause the dll unload in game process
            // UnhookWindowsHookEx(hHook);
            NativeLibrary.Free(hModule);
        }
    }
}