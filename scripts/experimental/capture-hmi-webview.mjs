import { writeFile } from 'node:fs/promises';

const [portArgument, outputPath] = process.argv.slice(2);
const port = Number(portArgument);
if (!Number.isInteger(port) || port < 1 || port > 65535 || !outputPath) {
  throw new Error('Usage: node capture-hmi-webview.mjs <DevTools port> <output.png>');
}

const endpoint = `http://127.0.0.1:${port}`;
const response = await fetch(`${endpoint}/json/list`, { signal: AbortSignal.timeout(5000) });
if (!response.ok) {
  throw new Error(`DevTools target discovery failed with HTTP ${response.status}.`);
}

const targets = await response.json();
const editors = targets.filter(target => target.type === 'page' && target.title === 'Hmi Web Editor');
if (editors.length !== 1 || !editors[0].webSocketDebuggerUrl) {
  throw new Error(`Expected one Hmi Web Editor target, found ${editors.length}.`);
}

const socketUrl = new URL(editors[0].webSocketDebuggerUrl);
if (socketUrl.protocol !== 'ws:' || socketUrl.hostname !== '127.0.0.1' || Number(socketUrl.port) !== port) {
  throw new Error('DevTools returned a WebSocket outside the selected loopback endpoint.');
}

const socket = new WebSocket(socketUrl);
try {
  const screenshot = await new Promise((resolve, reject) => {
    const abort = AbortSignal.timeout(15000);
    abort.addEventListener('abort', () => reject(new Error('DevTools screenshot timed out.')), { once: true });
    socket.addEventListener('error', () => reject(new Error('DevTools WebSocket failed.')), { once: true });
    socket.addEventListener('close', () => reject(new Error('DevTools WebSocket closed before returning a screenshot.')), { once: true });
    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({ id: 1, method: 'Page.captureScreenshot', params: { format: 'png' } }));
    }, { once: true });
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (message.id !== 1) return;
      if (message.error) {
        reject(new Error(`DevTools capture failed: ${message.error.message}`));
        return;
      }
      resolve(Buffer.from(message.result.data, 'base64'));
    });
  });
  await writeFile(outputPath, screenshot, { flag: 'wx' });
  console.log(`Wrote ${screenshot.length} bytes to ${outputPath}`);
} finally {
  socket.close();
}
