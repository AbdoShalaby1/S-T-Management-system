// Copies third-party browser assets into wwwroot/lib and fonts into wwwroot/fonts.
// Run through `npm run copy:vendor`. Missing optional assets warn instead of failing the build.
import { cp, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const project = join(root, '..');
const nm = join(project, 'node_modules');
const www = join(project, 'wwwroot');

const copied = [];
const missing = [];

async function copy(from, to) {
  if (!existsSync(from)) {
    missing.push(from);
    return;
  }
  await mkdir(dirname(to), { recursive: true });
  await cp(from, to, { recursive: true });
  copied.push(to);
}

async function main() {
  // FontAwesome
  const fa = join(nm, '@fortawesome/fontawesome-free');
  await copy(join(fa, 'css/all.min.css'), join(www, 'lib/fontawesome/css/all.min.css'));
  await copy(join(fa, 'webfonts'), join(www, 'lib/fontawesome/webfonts'));
  await copy(join(fa, 'LICENSE.txt'), join(www, 'lib/fontawesome/LICENSE.txt'));

  // Prism
  const prism = join(nm, 'prismjs');
  await copy(join(prism, 'prism.js'), join(www, 'lib/prism/prism.js'));
  await copy(join(prism, 'themes/prism-tomorrow.min.css'), join(www, 'lib/prism/prism-tomorrow.min.css'));
  for (const component of [
    'prism-csharp',
    'prism-json',
    'prism-markdown',
    'prism-bash'
  ]) {
    await copy(
      join(prism, `components/${component}.min.js`),
      join(www, `lib/prism/components/${component}.min.js`)
    );
  }
  await copy(
    join(prism, 'plugins/line-numbers/prism-line-numbers.min.js'),
    join(www, 'lib/prism/plugins/line-numbers.min.js')
  );
  await copy(
    join(prism, 'plugins/line-numbers/prism-line-numbers.min.css'),
    join(www, 'lib/prism/plugins/line-numbers.min.css')
  );
  await copy(
    join(prism, 'plugins/copy-to-clipboard/prism-copy-to-clipboard.min.js'),
    join(www, 'lib/prism/plugins/copy-to-clipboard.min.js')
  );

  // Fonts (self-hosted, latin subset)
  const faces = [
    ['@fontsource/inter', 'inter', 400],
    ['@fontsource/inter', 'inter', 500],
    ['@fontsource/inter', 'inter', 600],
    ['@fontsource/inter', 'inter', 700],
    ['@fontsource/jetbrains-mono', 'jetbrains-mono', 400],
    ['@fontsource/jetbrains-mono', 'jetbrains-mono', 500]
  ];

  for (const [pkg, family, weight] of faces) {
    const file = `${family}-latin-${weight}-normal.woff2`;
    await copy(join(nm, pkg, 'files', file), join(www, 'fonts', file));
  }

  console.log(`copied ${copied.length} assets`);
  if (missing.length > 0) {
    console.warn(`skipped ${missing.length} missing assets:`);
    for (const path of missing) console.warn(`  - ${path}`);
  }
}

await main();
