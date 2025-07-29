const express = require('express');
const fs = require('fs');
const fsp = fs.promises;
const path = require('path');
const os = require('os');
const { execSync } = require('child_process');

const app = express();
const PORT = process.env.PORT || 3000;

async function buildPackage() {
  const pkg = JSON.parse(await fsp.readFile(path.join(__dirname, 'package.json'), 'utf8'));
  const packageName = process.env.PACKAGE_NAME || pkg.name;
  const version = pkg.version;

  const zipFile = `${packageName}-${version}.zip`;
  const unityPackage = `${packageName}-${version}.unitypackage`;

  if (fs.existsSync(zipFile)) await fsp.unlink(zipFile);
  if (fs.existsSync(unityPackage)) await fsp.unlink(unityPackage);

  // Create zip archive of repository
  execSync(`zip -r ${zipFile} . -x '*.git*' '*node_modules*'`, { stdio: 'inherit' });

  // Find all meta files
  const metaList = execSync("find . -name '*.meta' -print").toString().trim().split('\n').filter(Boolean);
  const tmpDir = await fsp.mkdtemp(path.join(os.tmpdir(), 'unitypkg-'));

  for (const metaPath of metaList) {
    const assetPath = metaPath.replace(/\.meta$/, '');
    const metaContent = await fsp.readFile(metaPath, 'utf8');
    const match = metaContent.match(/guid:\s*(\S+)/);
    if (!match) continue;
    const guid = match[1];
    const outDir = path.join(tmpDir, guid);
    await fsp.mkdir(outDir, { recursive: true });
    if (fs.existsSync(assetPath) && fs.lstatSync(assetPath).isFile()) {
      await fsp.copyFile(assetPath, path.join(outDir, 'asset'));
    } else {
      await fsp.writeFile(path.join(outDir, 'asset'), '');
    }
    await fsp.copyFile(metaPath, path.join(outDir, 'asset.meta'));
    await fsp.writeFile(path.join(outDir, 'pathname'), assetPath.replace(/^\.\//, ''));
  }

  execSync(`tar -czf ${unityPackage} -C ${tmpDir} .`);
  await fsp.rm(tmpDir, { recursive: true, force: true });

  return { zipFile, unityPackage };
}

app.get('/build', async (req, res) => {
  try {
    const result = await buildPackage();
    res.json(result);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: String(err) });
  }
});

app.listen(PORT, () => {
  console.log(`Server listening on port ${PORT}`);
});
