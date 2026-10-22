// Publish a GitHub release for GraphEditor.
//
// Usage (from the repository root):
//     node tools\release.js [--draft] [--body <path-to-markdown>]
//
// What it does:
//   1. reads the version from src/GraphEditor/AssemblyInfo.cs
//   2. creates (or updates) the release for tag v<version>, using the tag that
//      must already exist on the remote
//   3. uploads build/GraphEditor.exe as a release asset (replacing an existing
//      asset with the same name)
//   4. prints the release URL, size and SHA-256 of the uploaded file
//
// Why Node instead of PowerShell: inside the AI sandbox the .NET/schannel TLS
// stack cannot reach the Windows credential store, so HTTPS calls fail with
// SEC_E_NO_CREDENTIALS. Node uses its own OpenSSL stack and works.
//
// The token is read from .git/github-token (fine-grained PAT, Contents: write).

const https = require('https');
const fs = require('fs');
const path = require('path');

const OWNER = 'I0me0I';
const REPO = 'GraphEditor';

function readVersion() {
  const file = path.join('src', 'GraphEditor', 'AssemblyInfo.cs');
  const text = fs.readFileSync(file, 'utf8');
  const m = text.match(/AssemblyInformationalVersion\("([^"]+)"\)/);
  if (!m) throw new Error('cannot find AssemblyInformationalVersion in ' + file);
  return m[1];
}

function request(method, url, headers, body) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    const opts = {
      hostname: u.hostname,
      path: u.pathname + u.search,
      method: method,
      headers: Object.assign({
        'User-Agent': 'GraphEditor-release-script',
        'Accept': 'application/vnd.github+json',
        'Authorization': 'token ' + token
      }, headers || {})
    };
    const req = https.request(opts, res => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => resolve({
        status: res.statusCode,
        headers: res.headers,
        body: Buffer.concat(chunks).toString('utf8')
      }));
    });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

function parse(text) {
  try { return JSON.parse(text); } catch (e) { return null; }
}

const token = fs.readFileSync(path.join('.git', 'github-token'), 'utf8').trim();

(async () => {
  const args = process.argv.slice(2);
  const draft = args.indexOf('--draft') >= 0;
  const bodyIndex = args.indexOf('--body');
  const version = readVersion();
  const tag = 'v' + version;

  const bodyFile = bodyIndex >= 0
    ? args[bodyIndex + 1]
    : path.join('build', 'release-body.md');
  if (!fs.existsSync(bodyFile)) {
    console.error('release notes not found: ' + bodyFile);
    process.exit(1);
  }
  const notes = fs.readFileSync(bodyFile, 'utf8');

  const exePath = path.join('build', 'GraphEditor.exe');
  if (!fs.existsSync(exePath)) {
    console.error('build output not found: ' + exePath + ' (run build.ps1 first)');
    process.exit(1);
  }

  const payload = JSON.stringify({
    tag_name: tag,
    name: 'GraphEditor ' + version,
    body: notes,
    draft: draft,
    prerelease: false
  });

  console.log('version = ' + version);
  console.log('tag     = ' + tag);
  console.log('notes   = ' + bodyFile + ' (' + notes.length + ' chars)');

  let res = await request('POST',
    'https://api.github.com/repos/' + OWNER + '/' + REPO + '/releases',
    { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(payload) },
    payload);

  let release = parse(res.body);
  console.log('create release -> HTTP ' + res.status);

  if (res.status === 422) {
    const get = await request('GET',
      'https://api.github.com/repos/' + OWNER + '/' + REPO + '/releases/tags/' + tag);
    release = parse(get.body);
    console.log('release already exists, updating it -> HTTP ' + get.status);
    const patch = await request('PATCH',
      'https://api.github.com/repos/' + OWNER + '/' + REPO + '/releases/' + release.id,
      { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(payload) },
      payload);
    release = parse(patch.body);
    console.log('update release -> HTTP ' + patch.status);
  } else if (res.status !== 201) {
    console.error('failed: ' + res.body);
    process.exit(1);
  }

  console.log('  id   = ' + release.id);
  console.log('  url  = ' + release.html_url);

  const file = fs.readFileSync(exePath);
  for (const a of (release.assets || []).filter(x => x.name === 'GraphEditor.exe')) {
    const del = await request('DELETE',
      'https://api.github.com/repos/' + OWNER + '/' + REPO + '/releases/assets/' + a.id);
    console.log('deleted old asset -> HTTP ' + del.status);
  }

  const uploadUrl = String(release.upload_url).replace('{?name,label}', '') +
    '?name=GraphEditor.exe&label=GraphEditor.exe';
  const up = await request('POST', uploadUrl,
    { 'Content-Type': 'application/octet-stream', 'Content-Length': file.length },
    file);
  const asset = parse(up.body);
  console.log('upload asset -> HTTP ' + up.status);
  if (asset && asset.browser_download_url) {
    console.log('  name     = ' + asset.name);
    console.log('  size     = ' + asset.size + ' bytes');
    console.log('  sha256   = ' + (asset.digest || '(not reported)'));
    console.log('  download = ' + asset.browser_download_url);
  } else {
    console.log('  body: ' + up.body);
  }
})().catch(e => { console.error('ERR ' + e.message); process.exit(1); });
