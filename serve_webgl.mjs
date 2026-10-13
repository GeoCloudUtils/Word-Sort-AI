import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { brotliDecompressSync } from "node:zlib";

const root = path.resolve("Webgl Build");
const port = 8000;

const contentTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "application/javascript",
  ".wasm": "application/wasm",
  ".json": "application/json",
  ".png": "image/png",
  ".ico": "image/x-icon",
};

const server = http.createServer((request, response) => {
  const requestPath = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
  const relativePath = requestPath === "/" ? "index.html" : requestPath.slice(1);
  const filePath = path.resolve(root, relativePath);

  if (!filePath.startsWith(root + path.sep) || !fs.existsSync(filePath)) {
    response.writeHead(404);
    response.end("Not found");
    return;
  }

  try {
    const compressed = filePath.endsWith(".br");
    const body = compressed ? brotliDecompressSync(fs.readFileSync(filePath)) : fs.readFileSync(filePath);
    const extension = path.extname(compressed ? filePath.slice(0, -3) : filePath);
    response.writeHead(200, {
      "Content-Type": contentTypes[extension] ?? "application/octet-stream",
      "Content-Length": body.length,
      "Cache-Control": "no-store",
    });
    response.end(body);
  } catch (error) {
    response.writeHead(500);
    response.end(String(error));
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Serving ${root} at http://localhost:${port}/`);
});
