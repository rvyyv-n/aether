using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Video;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Aether_App;

/// <summary>
/// Copy frame and Save frame (roadmap 7.19): mpv writes the current picture, with captions and without the on-screen
/// display, and it goes to the clipboard as an image or to the snapshot folder from Settings → Video. The mpv command
/// runs on a pool thread. UI thread only, apart from the pool work it starts.
/// </summary>
public sealed partial class MainPage
{
    private bool _frameBusy; // one grab at a time, so a held key does not queue a dozen

    private void OnCopyFrameMenuClick(object sender, RoutedEventArgs e) => _ = CopyFrameAsync();

    private void OnSaveFrameMenuClick(object sender, RoutedEventArgs e) => _ = SaveFrameAsync();

    private void OnCopyFrameAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CanGrabFrame())
        {
            args.Handled = true;
            _ = CopyFrameAsync();
        }
    }

    private void OnSaveFrameAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CanGrabFrame())
        {
            args.Handled = true;
            _ = SaveFrameAsync();
        }
    }

    /// <summary>A text box keeps Ctrl+C for copying its own text, so the key is left for it unhandled.</summary>
    private bool CanGrabFrame() =>
        _session is not null && HasVideo && !ArePlayerKeysPaused && !IsTextInputFocused();

    private bool IsTextInputFocused() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or RichEditBox or AutoSuggestBox;

    private async Task CopyFrameAsync()
    {
        if (_session is not { } session || _frameBusy)
        {
            return;
        }
        _frameBusy = true;
        string file = FrameGrab.TempFile(Path.GetTempPath(), Guid.NewGuid());
        try
        {
            byte[] image = await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                try
                {
                    session.Context.Command("screenshot-to-file", file, FrameGrab.Flags);
                    return File.ReadAllBytes(file);
                }
                finally
                {
                    TryDelete(file);
                }
            });
            if (!_isLoaded)
            {
                return;
            }
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(image.AsBuffer());
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            try
            {
                Clipboard.Flush(); // the picture stays on the clipboard after Aether closes
                stream.Dispose();
            }
            catch (COMException)
            {
                // Another app holds the clipboard; the picture is still there for a paste while Aether runs.
            }
            _toasts?.Show("frame-copied", AppStrings.Get("Toast_FrameCopied"));
        }
        catch (Exception ex) when (IsFrameFailure(ex))
        {
            _toasts?.Show("frame-failed", AppStrings.Get("Toast_FrameCopyFailed"), ToastTone.Failure);
        }
        finally
        {
            _frameBusy = false;
        }
    }

    private async Task SaveFrameAsync()
    {
        if (_session is not { } session || _frameBusy)
        {
            return;
        }
        _frameBusy = true;
        try
        {
            string configured = App.Settings.Current.ScreenshotDirectory;
            string folder = FrameGrab.SaveFolder(configured, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            if (folder.Length == 0)
            {
                throw new IOException("No folder to save the frame in.");
            }
            bool useDefaultFolder = string.IsNullOrWhiteSpace(configured);
            // A little early, so a file stamped just before the command returns is not missed by a coarse clock.
            DateTime since = DateTime.UtcNow.AddSeconds(-2);
            string? saved = await Task.Run(() =>
            {
                if (!FrameGrab.IsMpvPath(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                if (useDefaultFolder)
                {
                    session.Context.SetProperty("screenshot-directory", folder);
                }
                session.Context.Command("screenshot", FrameGrab.Flags);
                return FrameGrab.IsMpvPath(folder) ? null : FrameGrab.FindSaved(folder, since);
            });
            if (!_isLoaded)
            {
                return;
            }
            string text = AppStrings.Get("Toast_FrameSaved");
            if (saved is null)
            {
                _toasts?.Show("frame-saved", text, ToastTone.Success);
            }
            else
            {
                _toasts?.Show("frame-saved", text, ToastTone.Success,
                    AppStrings.Get("Toast_ShowInFolder"), () => _ = ShowInFolder.TryAsync(saved));
            }
        }
        catch (Exception ex) when (IsFrameFailure(ex))
        {
            _toasts?.Show("frame-failed", AppStrings.Get("Toast_FrameSaveFailed"), ToastTone.Failure);
        }
        finally
        {
            _frameBusy = false;
        }
    }

    private static bool IsFrameFailure(Exception ex) =>
        ex is MpvException or IOException or UnauthorizedAccessException or ObjectDisposedException or COMException;

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary picture is harmless; the temp folder is cleaned by Windows.
        }
    }
}
