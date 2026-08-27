const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");

const prototypeRoot = __dirname;
const repoRoot = path.resolve(prototypeRoot, "..", "..");
const outputArgument = process.argv.find(argument => argument.startsWith("--output="));
const outputRoot = outputArgument
  ? path.resolve(outputArgument.slice("--output=".length))
  : path.join(repoRoot, "output", "playwright", "event-cg-scene-v2");
const prototypeUrl = pathToFileURL(path.join(prototypeRoot, "index.html"));

const captureCases = [
  ...[1, 2, 3, 4, 5, 6, 7, 8].map(count => ({
    name: `victory-${count}p-1280x720`, scene: "victory", count, motion: "full", width: 1280, height: 720
  })),
  { name: "opening-4p-1280x720", scene: "opening", count: 4, motion: "full", width: 1280, height: 720 },
  { name: "midas-4p-1280x720", scene: "midas", count: 4, motion: "full", width: 1280, height: 720 },
  { name: "ritual-6p-1280x720", scene: "ritual", count: 6, motion: "full", width: 1280, height: 720 },
  { name: "curse-6p-reduced-1280x720", scene: "curse", count: 6, motion: "reduced", width: 1280, height: 720 },
  { name: "defeat-4p-1280x720", scene: "defeat", count: 4, motion: "full", width: 1280, height: 720 },
  { name: "settlement-8p-1280x720", scene: "settlement", count: 8, motion: "full", width: 1280, height: 720 },
  { name: "victory-8p-922x838", scene: "victory", count: 8, motion: "full", width: 922, height: 838 },
  { name: "opening-4p-922x838", scene: "opening", count: 4, motion: "reduced", width: 922, height: 838 },
  { name: "settlement-8p-safe-922x838", scene: "settlement", count: 8, motion: "reduced", safeZone: true, width: 922, height: 838 }
];

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

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
    const candidates = process.platform === "win32"
      ? [
          "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
          "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
          "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
          "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe"
        ]
      : ["/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"];
    const executablePath = candidates.find(candidate => fs.existsSync(candidate));
    if (!executablePath) throw error;
    return chromium.launch({ headless: true, executablePath });
  }
}

async function waitUntilReady(page) {
  await page.waitForFunction(() => document.body.dataset.previewReady === "true");
  await page.evaluate(async () => {
    await Promise.all([...document.images].map(image => image.decode?.().catch(() => undefined)));
  });
}

async function validate(page, captureCase) {
  return page.evaluate(expected => {
    const scene = document.querySelector("#scene");
    const shell = document.querySelector(".prototype-shell");
    const participants = [...document.querySelectorAll(".participant")];
    const sceneRect = scene.getBoundingClientRect();
    const shellRect = shell.getBoundingClientRect();
    const nameplates = [...document.querySelectorAll(".participant figcaption")];
    const errors = [];
    const inside = (inner, outer, tolerance = 1.5) =>
      inner.left >= outer.left - tolerance
      && inner.top >= outer.top - tolerance
      && inner.right <= outer.right + tolerance
      && inner.bottom <= outer.bottom + tolerance;

    if (document.documentElement.scrollWidth > innerWidth + 1) errors.push("horizontal page overflow");
    if (document.documentElement.scrollHeight > innerHeight + 1) errors.push("vertical page overflow");
    if (!inside(shellRect, { left: 0, top: 0, right: innerWidth, bottom: innerHeight })) errors.push("prototype shell leaves viewport");
    if (Math.abs(sceneRect.width / sceneRect.height - 16 / 9) > .02) errors.push("scene aspect ratio drifted from 16:9");
    if (participants.length !== expected.count) errors.push(`expected ${expected.count} participants, found ${participants.length}`);
    if ([...document.images].some(image => !image.complete || image.naturalWidth <= 0)) errors.push("one or more visual assets did not load");
    for (const participant of participants) {
      if (!inside(participant.getBoundingClientRect(), sceneRect, 2)) {
        errors.push(`participant leaves scene: ${participant.dataset.index}`);
      }
    }
    for (const nameplate of nameplates) {
      if (!inside(nameplate.getBoundingClientRect(), sceneRect, 2)) {
        errors.push(`nameplate leaves scene: ${nameplate.textContent.trim()}`);
      }
    }
    for (let leftIndex = 0; leftIndex < nameplates.length; leftIndex += 1) {
      const leftRect = nameplates[leftIndex].getBoundingClientRect();
      for (let rightIndex = leftIndex + 1; rightIndex < nameplates.length; rightIndex += 1) {
        const rightRect = nameplates[rightIndex].getBoundingClientRect();
        const overlapWidth = Math.min(leftRect.right, rightRect.right) - Math.max(leftRect.left, rightRect.left);
        const overlapHeight = Math.min(leftRect.bottom, rightRect.bottom) - Math.max(leftRect.top, rightRect.top);
        if (overlapWidth > 2 && overlapHeight > 2) {
          errors.push(`nameplates overlap: ${nameplates[leftIndex].textContent.trim()} / ${nameplates[rightIndex].textContent.trim()}`);
        }
      }
    }

    const expectedLayout = expected.count >= 7 ? "panels" : "tableau";
    if (scene.dataset.layout !== expectedLayout) errors.push(`expected ${expectedLayout} layout, found ${scene.dataset.layout}`);
    if (expected.motion === "reduced" && !scene.classList.contains("reduced-motion")) errors.push("reduced motion state not applied");
    if (expected.safeZone && !scene.classList.contains("show-safe-zone")) errors.push("safe-zone state not applied");

    for (const element of document.querySelectorAll("button, .titlebar h1, .titlebar p, .status span, .scene-caption strong, .scene-caption span, .participant figcaption, .diagnostics span")) {
      if (element.scrollWidth > element.clientWidth + 2 || element.scrollHeight > element.clientHeight + 2) {
        errors.push(`text overflow: ${element.textContent.trim()}`);
      }
    }

    return {
      errors,
      participantCount: participants.length,
      layout: scene.dataset.layout,
      sceneWidth: Math.round(sceneRect.width),
      sceneHeight: Math.round(sceneRect.height)
    };
  }, captureCase);
}

async function main() {
  fs.mkdirSync(outputRoot, { recursive: true });
  const browser = await launchBrowser();
  const results = [];
  const allErrors = [];
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    await page.goto(prototypeUrl.href, { waitUntil: "load" });
    await waitUntilReady(page);

    for (const captureCase of captureCases) {
      await page.setViewportSize({ width: captureCase.width, height: captureCase.height });
      await page.evaluate(next => window.EventCgPrototype.setState(next), captureCase);
      await waitUntilReady(page);
      await page.waitForTimeout(captureCase.motion === "reduced" ? 60 : 900);
      const validation = await validate(page, captureCase);
      const screenshot = path.join(outputRoot, `${captureCase.name}.png`);
      await page.screenshot({ path: screenshot, animations: "disabled" });
      const bytes = fs.readFileSync(screenshot);
      if (bytes.length < 30000) validation.errors.push("screenshot contains too little visual data");
      const sha256 = crypto.createHash("sha256").update(bytes).digest("hex");
      for (const error of validation.errors) allErrors.push(`${captureCase.name}: ${error}`);
      results.push({ ...captureCase, screenshot, sha256, ...validation });
    }

    const duplicateGroups = new Map();
    for (const result of results) {
      const names = duplicateGroups.get(result.sha256) || [];
      names.push(result.name);
      duplicateGroups.set(result.sha256, names);
    }
    for (const names of duplicateGroups.values()) {
      if (names.length > 1) allErrors.push(`duplicate screenshots: ${names.join(", ")}`);
    }
  }
  finally {
    await browser.close();
  }

  const report = {
    passed: allErrors.length === 0,
    captures: results.length,
    generatedAtUtc: new Date().toISOString(),
    errors: allErrors,
    results
  };
  fs.writeFileSync(path.join(outputRoot, "report.json"), JSON.stringify(report, null, 2));
  assert(report.passed, allErrors.join("; "));
  console.log(`Event CG scene v2 prototype passed: ${results.length} captures.`);
}

main().catch(error => {
  console.error(error.stack || error);
  process.exitCode = 1;
});
