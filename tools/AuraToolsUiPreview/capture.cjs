const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");

const previewRoot = __dirname;
const repoRoot = path.resolve(previewRoot, "..", "..");
const outputArgument = process.argv.find(argument => argument.startsWith("--output="));
const outputRoot = outputArgument
  ? path.resolve(outputArgument.slice("--output=".length))
  : path.join(repoRoot, "output", "playwright", "aura-tools-toolbox");
const previewUrl = pathToFileURL(path.join(previewRoot, "index.html"));

const captureCases = [
  { name: "default-920x848", scenario: "default", width: 920, height: 848 },
  { name: "default-1280x720", scenario: "default", width: 1280, height: 720 },
  { name: "default-1280x800", scenario: "default", width: 1280, height: 800 },
  { name: "default-1600x900", scenario: "default", width: 1600, height: 900 },
  { name: "default-1920x1080", scenario: "default", width: 1920, height: 1080 },
  { name: "long-text-1280x720", scenario: "long-text", width: 1280, height: 720 },
  { name: "warning-1280x720", scenario: "warning", width: 1280, height: 720 },
  { name: "empty-1280x720", scenario: "empty", width: 1280, height: 720 },
  { name: "extensions-1280x720", scenario: "extensions", width: 1280, height: 720 },
  { name: "compact-1024x640", scenario: "default", width: 1024, height: 640 }
];

async function launchBrowser() {
  const requestedExecutable = process.env.AURA_PREVIEW_BROWSER_EXECUTABLE;
  if (requestedExecutable) {
    return chromium.launch({ headless: true, executablePath: requestedExecutable });
  }
  try {
    return await chromium.launch({ headless: true });
  }
  catch (error) {
    if (!String(error.message || error).includes("Executable doesn't exist")) throw error;
    const systemBrowsers = process.platform === "win32"
      ? [
          "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
          "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
          "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
          "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe"
        ]
      : ["/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"];
    const executablePath = systemBrowsers.find(candidate => fs.existsSync(candidate));
    if (!executablePath) throw error;
    return chromium.launch({ headless: true, executablePath });
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function assertSourceContract() {
  const files = {
    ui: fs.readFileSync(path.join(repoRoot, "AuraToolsExp-Dev", "Features", "Settings", "AuraToolsUi.cs"), "utf8"),
    theme: fs.readFileSync(path.join(repoRoot, "AuraToolsExp-Dev", "Features", "Settings", "AuraToolsUiTheme.cs"), "utf8"),
    visualSpec: fs.readFileSync(path.join(repoRoot, "AuraToolsExp-Dev", "Features", "Settings", "ToolboxVisualSpec.cs"), "utf8"),
    shell: fs.readFileSync(path.join(repoRoot, "AuraToolsExp-Dev", "Features", "Settings", "ToolboxSettingsShell.cs"), "utf8"),
    ids: fs.readFileSync(path.join(repoRoot, "AuraToolsExp-Dev", "Modules", "AuraToolModuleIds.cs"), "utf8")
  };
  const required = [
    [files.visualSpec, "CategoryWidth = 168f", "preview category width drifted from production"],
    [files.visualSpec, "HeaderHeight = 60f", "preview header height drifted from production"],
    [files.visualSpec, "ModuleRowHeight = 78f", "preview row height drifted from production"],
    [files.visualSpec, "0.031f, 0.016f, 0.227f, 1f", "preview background token drifted from production"],
    [files.shell, "ToolboxCategoryRail.Create", "production category rail is missing"],
    [files.shell, "!string.IsNullOrWhiteSpace(search)", "production search no longer spans categories"]
  ];
  for (const [source, token, message] of required) assert(source.includes(token), message);
  const moduleIds = [...files.ids.matchAll(/public const string \w+ = "([^"]+)";/g)].map(match => match[1]);
  assert(moduleIds.length === 23, `expected 23 production module ids, found ${moduleIds.length}`);
  return moduleIds.sort();
}

async function waitUntilReady(page) {
  await page.waitForFunction(() => document.body.dataset.previewReady === "true");
  await page.evaluate(async () => {
    await Promise.all([...document.images].map(image => {
      if (image.complete) return Promise.resolve();
      return new Promise(resolve => {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", resolve, { once: true });
      });
    }));
    if (document.fonts?.ready) await document.fonts.ready;
  });
  await page.waitForTimeout(40);
}

async function openScenario(page, captureCase) {
  await page.setViewportSize({ width: captureCase.width, height: captureCase.height });
  const url = new URL(previewUrl.href);
  url.searchParams.set("scenario", captureCase.scenario);
  url.searchParams.set("capture", "1");
  await page.goto(url.href, { waitUntil: "load" });
  await waitUntilReady(page);
}

async function captureScenario(page, captureCase, report) {
  await openScenario(page, captureCase);
  const validation = await page.evaluate(() => window.__AURA_PREVIEW__.validate());
  const screenshotPath = path.join(outputRoot, `${captureCase.name}.png`);
  await page.screenshot({ path: screenshotPath, animations: "disabled" });
  report.captures.push({ ...captureCase, screenshot: screenshotPath, validation });
  assert(validation.ok, `${captureCase.name}: ${validation.errors.join("; ")}`);
}

async function verifyInteractions(page, productionModuleIds, report) {
  await openScenario(page, { scenario: "default", width: 1280, height: 720 });
  const previewIds = (await page.evaluate(() => window.__AURA_PREVIEW__.visibleModuleIds())).sort();
  assert(JSON.stringify(previewIds) === JSON.stringify(productionModuleIds), "preview module inventory differs from production ids");

  await page.locator('[data-category-id="presentation"]').click();
  await waitUntilReady(page);
  const presentationIds = await page.evaluate(() => window.__AURA_PREVIEW__.visibleModuleIds());
  assert(presentationIds.length === 9, "presentation category should contain nine tools");
  assert(["presentation.skill-cg", "presentation.card-use-cg", "presentation.event-cg"]
    .every(id => presentationIds.includes(id)), "presentation category must expose Role, Card, and Event CG");
  assert(!presentationIds.includes("presentation.feast-cg"), "legacy Feast CG must remain hidden from the toolbox");
  const presentationPath = path.join(outputRoot, "interaction-presentation-cg.png");
  await page.screenshot({ path: presentationPath, animations: "disabled" });
  await page.locator("#module-list").evaluate(element => { element.scrollTop = element.scrollHeight; });
  await page.waitForTimeout(40);
  const presentationBottomPath = path.join(outputRoot, "interaction-presentation-cg-bottom.png");
  await page.screenshot({ path: presentationBottomPath, animations: "disabled" });

  await page.locator('[data-category-id="records"]').click();
  await waitUntilReady(page);
  assert(await page.locator(".module-row").count() === 3, "records category should contain three tools");

  await page.locator('[data-category-id="multiplayer"]').click();
  await waitUntilReady(page);
  assert(await page.locator(".module-row").count() === 2, "multiplayer category should contain two tools");

  await page.locator('[data-category-id="intelligence"]').click();
  await waitUntilReady(page);
  assert(await page.locator(".module-row").count() === 2, "intelligence category should separate auto battle and the strategy lab");
  assert(await page.locator('[data-module-id="intelligence.strategy-model-lab"] .toolbox-checkbox').count() === 0,
    "strategy model lab should not expose a duplicate enable switch");

  await page.locator('[data-category-id="system"]').click();
  await waitUntilReady(page);
  assert(await page.locator(".module-row").count() === 3, "system category should contain three tools");

  await page.locator("#search-input").fill("皮肤");
  await waitUntilReady(page);
  const searchIds = await page.evaluate(() => window.__AURA_PREVIEW__.visibleModuleIds());
  assert(searchIds.length === 1 && searchIds[0] === "presentation.skin", "search should span the selected category");
  assert(await page.locator('[data-category-id="all"]').getAttribute("aria-pressed") === "true", "global search should project the All category state");
  const searchPath = path.join(outputRoot, "interaction-global-search.png");
  await page.screenshot({ path: searchPath, animations: "disabled" });

  const settingsButton = page.locator(".module-settings").first();
  await settingsButton.click();
  assert(await page.locator("#settings-overlay").isVisible(), "settings overlay did not open");
  const overlayPath = path.join(outputRoot, "interaction-settings-overlay.png");
  await page.screenshot({ path: overlayPath, animations: "disabled" });
  await page.locator("#close-overlay").click();
  assert(!(await page.locator("#settings-overlay").isVisible()), "settings overlay did not close");
  assert(await settingsButton.evaluate(element => document.activeElement === element), "settings focus was not restored after closing overlay");

  await page.locator("#clear-search").click();
  await waitUntilReady(page);
  await page.locator('[data-category-id="all"]').click();
  await waitUntilReady(page);
  const firstSwitch = page.locator('.module-row [role="checkbox"]').first();
  const before = await firstSwitch.getAttribute("aria-checked");
  await firstSwitch.click();
  const after = await firstSwitch.getAttribute("aria-checked");
  assert(before !== after, "module checkbox did not update in place");
  assert(await firstSwitch.evaluate(element => document.activeElement === element), "module checkbox lost focus after update");

  const allCategory = page.locator('[data-category-id="all"]');
  await allCategory.focus();
  await page.keyboard.press("ArrowDown");
  assert(await page.locator('[data-category-id="gameplay"]').evaluate(element => document.activeElement === element), "category arrow navigation failed");

  report.interactions = {
    globalSearchScreenshot: searchPath,
    overlayScreenshot: overlayPath,
    presentationScreenshot: presentationPath,
    presentationBottomScreenshot: presentationBottomPath,
    moduleInventory: productionModuleIds,
    passed: true
  };
}

async function main() {
  fs.mkdirSync(outputRoot, { recursive: true });
  const report = {
    generatedAtUtc: new Date().toISOString(),
    outputRoot,
    captures: [],
    interactions: null,
    consoleErrors: []
  };
  const productionModuleIds = assertSourceContract();
  const browser = await launchBrowser();
  try {
    const context = await browser.newContext({
      colorScheme: "dark",
      locale: "zh-CN",
      reducedMotion: "reduce",
      deviceScaleFactor: 1
    });
    const page = await context.newPage();
    page.on("pageerror", error => report.consoleErrors.push(error.message));
    page.on("console", message => {
      if (message.type() === "error") report.consoleErrors.push(message.text());
    });

    for (const captureCase of captureCases) {
      await captureScenario(page, captureCase, report);
    }
    await verifyInteractions(page, productionModuleIds, report);
    assert(report.consoleErrors.length === 0, `browser errors: ${report.consoleErrors.join("; ")}`);
    await context.close();
  }
  finally {
    await browser.close();
    fs.writeFileSync(path.join(outputRoot, "report.json"), `${JSON.stringify(report, null, 2)}\n`, "utf8");
  }
  console.log(`AuraTools toolbox preview passed: ${captureCases.length} captures and interaction checks.`);
  console.log(`Output: ${outputRoot}`);
}

main().catch(error => {
  console.error(error.stack || error.message || String(error));
  process.exitCode = 1;
});
