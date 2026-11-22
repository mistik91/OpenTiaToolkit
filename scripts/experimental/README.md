# Capture the Unified HMI engineering editor

`open-tia-portal --hmi-capture=true` and `capture-hmi-screen` provide the toolkit workflow. See [the CLI guide](../../docs/CLI_USAGE.md#capture-the-next-gen-screen-editor). The manual prototype below records how the WebView capability was first verified.

`capture-hmi-webview.mjs` captures the open WinCC Unified Screen Editor (Next Gen.) viewport through Edge WebView2. It uses the engineering renderer and can run without compiling the project or starting WinCC Runtime.

## Reproduce the experiment

1. Launch a separate TIA Portal V21 instance with a disposable project. Set `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` to `--remote-debugging-port=49321 --remote-debugging-address=127.0.0.1` and `WEBVIEW2_USER_DATA_FOLDER` to a fresh directory in that process's environment. Use an available port and verify that the listener belongs to the test WebView2 process and is bound to loopback.
2. Open a Unified screen in the Next Gen editor. Make the canvas visible by collapsing the inspector if needed. Select **Fit to screen** for a whole-screen view.
3. Run the following command with a Node.js release that includes the built-in `WebSocket` API. The experiment used Node.js 24.15.0.

   ```powershell
   node scripts/experimental/capture-hmi-webview.mjs 49321 HmiPreview.png
   ```

4. Capture again after an Openness edit, using a different output filename. Close the isolated TIA instance when finished to stop the debugging listener.

The script expects one `Hmi Web Editor` target and uses `Page.captureScreenshot`. The PNG contains the WebView viewport at the current zoom, including scrollbars and any editor overlays. At 100% zoom a large screen may extend beyond the viewport.

## Live results

On TIA Portal V21 Update 2, the `DarkModeDemo` screen in a disposable copy of `HmiTest` rendered without a compile or a Runtime launch. The 549 × 384 PNGs taken in the foreground and after switching foreground focus to a different TIA instance were byte-identical.

A targeted OpenTiaToolkit patch changed `HomeButton.Text` from `HOMELESS` to `WEBVIEW TEST`. The open editor updated while it was in the background. The next PNG differed by 72 pixels, all within the button caption. Reverting the patch restored the original filtered screen hash and byte-identical PNG. The copy was closed without saving.

Capture timed out while the inspector covered the viewport. Minimized-window capture, automated screen selection and zoom, and Windows 10 compatibility remain untested.

## WithoutUserInterface check

A separate V21 Update 2 instance started in `WithoutUserInterface` mode opened a fresh disposable copy of `HmiTest` and exported 29 items from `DarkModeDemo` through Openness. The process had no main window and spawned no Edge WebView2 process. The installed V21 public `HmiScreen` type does not implement `IShowable` or expose an editor-opening method. The check confirmed headless screen data access and did not find a public path to instantiate the Next Gen editor in that session.
