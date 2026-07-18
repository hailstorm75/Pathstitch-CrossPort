using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Pathstitch.App.Services;

public enum MacOSAppIconVariant
{
    Light,
    Dark,
}

public enum MacOSFinderPreviewFormat
{
    Dxf = 0,
    Step = 1,
    Stch = 2,
}

public static class MacOSAppIconResolver
{
    public static MacOSAppIconVariant Resolve(string? choice, bool systemUsesDarkAppearance)
        => choice?.Trim().ToLowerInvariant() switch
        {
            "light" => MacOSAppIconVariant.Light,
            "dark" => MacOSAppIconVariant.Dark,
            _ => systemUsesDarkAppearance ? MacOSAppIconVariant.Dark : MacOSAppIconVariant.Light,
        };

    public static string GetAssetFileName(MacOSAppIconVariant variant)
        => variant == MacOSAppIconVariant.Dark ? "AppIconDark.png" : "AppIconLight.png";
}

public static class MacOSIntegrationService
{
    private const string LibraryName = "libPathstitchMacBridge.dylib";
    private static readonly object Gate = new();
    private static bool _initializationAttempted;
    private static IntPtr _libraryHandle;
    private static SetQuickLookPreferencesDelegate? _setQuickLookPreferences;
    private static GetQuickLookPreferenceDelegate? _getQuickLookPreference;
    private static ApplyAppIconDelegate? _applyAppIcon;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetQuickLookPreferencesDelegate(int dxfEnabled, int stepEnabled, int stchEnabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetQuickLookPreferenceDelegate(int format);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ApplyAppIconDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string choice);

    public static bool TryApply(UserPreferences preferences)
    {
        var previewsApplied = TrySetQuickLookPreferences(
            preferences.FinderPreviewDxf,
            preferences.FinderPreviewStep,
            preferences.FinderPreviewStch);
        var iconApplied = TryApplyDockIcon(preferences.AppIcon);
        return previewsApplied && iconApplied;
    }

    public static bool TryApplyDockIcon(string? choice)
    {
        if (!TryInitialize() || _applyAppIcon is null)
            return false;

        try
        {
            return _applyAppIcon(choice ?? "Automatic") != 0;
        }
        catch (Exception exception) when (
            exception is SEHException
            or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TrySetQuickLookPreferences(
        bool dxfEnabled,
        bool stepEnabled,
        bool stchEnabled)
    {
        if (!TryInitialize() || _setQuickLookPreferences is null)
            return false;

        try
        {
            return _setQuickLookPreferences(
                dxfEnabled ? 1 : 0,
                stepEnabled ? 1 : 0,
                stchEnabled ? 1 : 0) != 0;
        }
        catch (Exception exception) when (
            exception is SEHException
            or InvalidOperationException)
        {
            return false;
        }
    }


    public static bool TryGetQuickLookPreference(
        MacOSFinderPreviewFormat format,
        out bool enabled)
    {
        enabled = false;
        if (!TryInitialize() || _getQuickLookPreference is null)
            return false;

        try
        {
            var value = _getQuickLookPreference((int)format);
            if (value < 0)
                return false;

            enabled = value != 0;
            return true;
        }
        catch (Exception exception) when (
            exception is SEHException
            or InvalidOperationException)
        {
            return false;
        }
    }
    public static IReadOnlyList<string> GetCandidateLibraryPaths(string applicationBaseDirectory)
        =>
        [
            Path.Combine(applicationBaseDirectory, LibraryName),
            Path.GetFullPath(Path.Combine(
                applicationBaseDirectory,
                "..",
                "Frameworks",
                LibraryName)),
        ];

    private static bool TryInitialize()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        lock (Gate)
        {
            if (_initializationAttempted)
                return _libraryHandle != IntPtr.Zero
                    && _setQuickLookPreferences is not null
                    && _getQuickLookPreference is not null
                    && _applyAppIcon is not null;

            _initializationAttempted = true;
            foreach (var candidate in GetCandidateLibraryPaths(AppContext.BaseDirectory))
            {
                try
                {
                    if (!NativeLibrary.TryLoad(candidate, out var handle))
                        continue;
                    if (!NativeLibrary.TryGetExport(
                            handle,
                            "pathstitch_set_quicklook_preferences",
                            out var quickLookExport)
                        || !NativeLibrary.TryGetExport(
                            handle,
                            "pathstitch_get_quicklook_preference",
                            out var getQuickLookExport)
                        || !NativeLibrary.TryGetExport(
                            handle,
                            "pathstitch_apply_app_icon",
                            out var appIconExport))
                    {
                        NativeLibrary.Free(handle);
                        continue;
                    }

                    _setQuickLookPreferences =
                        Marshal.GetDelegateForFunctionPointer<SetQuickLookPreferencesDelegate>(quickLookExport);
                    _getQuickLookPreference =
                        Marshal.GetDelegateForFunctionPointer<GetQuickLookPreferenceDelegate>(getQuickLookExport);
                    _applyAppIcon =
                        Marshal.GetDelegateForFunctionPointer<ApplyAppIconDelegate>(appIconExport);
                    _libraryHandle = handle;
                    return true;
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or DllNotFoundException
                    or EntryPointNotFoundException)
                {
                    // Native integration stays optional outside packaged macOS builds.
                }
            }

            return false;
        }
    }
}
