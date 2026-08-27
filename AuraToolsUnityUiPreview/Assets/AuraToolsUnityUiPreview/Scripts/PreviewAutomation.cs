using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace AuraTools.UnityUiPreview
{
    [Serializable]
    internal sealed class PreviewCaptureResult
    {
        public string name;
        public string file;
        public int width;
        public int height;
        public int nonBackgroundSamples;
        public int probePixels;
        public int edgeAccentPixels;
        public string sha256;
        public string[] errors;
    }

    [Serializable]
    internal sealed class PreviewCaptureReport
    {
        public bool passed;
        public int captures;
        public string generatedAtUtc;
        public string unityVersion;
        public string[] errors;
        public PreviewCaptureResult[] results;
    }

    public sealed class PreviewAutomation : MonoBehaviour
    {
        private sealed class CaptureCase
        {
            internal string Name;
            internal int Tab;
            internal string Scenario;
            internal int Width;
            internal int Height;
            internal bool Toolbox;
            internal bool Overlay;
            internal string OverlayKind;
            internal string Category;
        }

        private SettingsPreviewController controller;

        private IEnumerator Start()
        {
            controller = FindObjectOfType<SettingsPreviewController>();
            if (!HasArgument("-previewAutoCapture"))
            {
                yield break;
            }

            yield return null;
            yield return null;
            yield return RunCaptureSuite();
        }

        private IEnumerator RunCaptureSuite()
        {
            var output = ArgumentValue("-previewCaptureOutput=");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.Combine(Application.persistentDataPath, "PreviewCaptures");
            }
            Directory.CreateDirectory(output);
            controller.SetPreviewChromeVisible(false);

            var cases = new[]
            {
                Case("native-audio-visual-1280x720", 0, "default", 1280, 720),
                Case("native-game-1280x720", 1, "default", 1280, 720),
                Case("native-feedback-1280x720", 2, "default", 1280, 720),
                Case("native-keys-1280x720", 3, "default", 1280, 720),
                Case("toolbox-default-1280x720", 4, "default", 1280, 720, true),
                Case("toolbox-warning-1280x720", 4, "warning", 1280, 720, true),
                Case("toolbox-long-text-1280x720", 4, "long-text", 1280, 720, true),
                Case("toolbox-empty-1280x720", 4, "empty", 1280, 720, true),
                Case("toolbox-extensions-1280x720", 4, "extensions", 1280, 720, true),
                Case("toolbox-records-1280x720", 4, "default", 1280, 720, true, false, "records"),
                Case("toolbox-multiplayer-1280x720", 4, "default", 1280, 720, true, false, "multiplayer"),
                Case("toolbox-intelligence-1280x720", 4, "default", 1280, 720, true, false, "intelligence"),
                Case("toolbox-system-1280x720", 4, "default", 1280, 720, true, false, "system"),
                Case("toolbox-default-922x838", 4, "default", 922, 838, true),
                Case("toolbox-compact-1024x640", 4, "default", 1024, 640, true),
                Case("toolbox-overlay-1280x720", 4, "default", 1280, 720, true, true),
                Case("role-cg-1280x720", 4, "default", 1280, 720, true, true, "all", "role-cg"),
                Case("role-cg-922x838", 4, "default", 922, 838, true, true, "all", "role-cg"),
                Case("event-cg-config-1280x720", 4, "default", 1280, 720, true, true, "all", "event-cg"),
                Case("event-cg-config-922x838", 4, "default", 922, 838, true, true, "all", "event-cg"),
                Case("event-cg-preview-1280x720", 4, "default", 1280, 720, true, true, "all", "event-cg-preview"),
                Case("event-cg-preview-922x838", 4, "default", 922, 838, true, true, "all", "event-cg-preview")
            };
            var results = new List<PreviewCaptureResult>();
            var allErrors = new List<string>();
            allErrors.AddRange(controller.ValidateNativeVisualLanguage());

            foreach (var captureCase in cases)
            {
                controller.SetToolboxScenario(captureCase.Scenario);
                controller.SetToolboxCategory(captureCase.Category);
                controller.SelectTab(captureCase.Tab);
                if (captureCase.Overlay)
                {
                    if (string.IsNullOrWhiteSpace(captureCase.OverlayKind))
                    {
                        controller.ShowToolSettings("角色皮肤", "已启用 3/3 个候选皮肤", "管理已注册皮肤并选择本地显示效果。");
                    }
                    else
                    {
                        controller.ShowCgSettingsPreview(captureCase.OverlayKind);
                    }
                }
                yield return null;
                yield return new WaitForEndOfFrame();
                Canvas.ForceUpdateCanvases();

                var errors = new List<string>();
                ValidateSelection(captureCase, errors);
                if (captureCase.Toolbox && !captureCase.Overlay)
                {
                    errors.AddRange(controller.ValidateToolbox());
                }

                var path = Path.Combine(output, captureCase.Name + ".png");
                var imageMetrics = Capture(controller, path, captureCase.Width, captureCase.Height);
                if (imageMetrics.NonBackgroundSamples < 1000)
                {
                    errors.Add("capture contains too little rendered UI");
                }
                if (captureCase.Toolbox && imageMetrics.ProbePixels > 0)
                {
                    errors.Add("native leak probe is visible through the toolbox");
                }
                if (imageMetrics.EdgeAccentPixels > 0)
                {
                    errors.Add("settings frame touches or leaves the capture boundary");
                }
                foreach (var error in errors)
                {
                    allErrors.Add(captureCase.Name + ": " + error);
                }
                results.Add(new PreviewCaptureResult
                {
                    name = captureCase.Name,
                    file = path,
                    width = captureCase.Width,
                    height = captureCase.Height,
                    nonBackgroundSamples = imageMetrics.NonBackgroundSamples,
                    probePixels = imageMetrics.ProbePixels,
                    edgeAccentPixels = imageMetrics.EdgeAccentPixels,
                    sha256 = HashFile(path),
                    errors = errors.ToArray()
                });
                yield return null;
            }

            foreach (var duplicate in results.GroupBy(result => result.sha256).Where(group => group.Count() > 1))
            {
                allErrors.Add("duplicate captures: " + string.Join(", ", duplicate.Select(result => result.name).ToArray()));
            }

            var report = new PreviewCaptureReport
            {
                passed = allErrors.Count == 0,
                captures = results.Count,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                errors = allErrors.ToArray(),
                results = results.ToArray()
            };
            File.WriteAllText(
                Path.Combine(output, "report.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("AuraTools Unity UI preview captures=" + report.captures
                      + ", passed=" + report.passed
                      + ", output=" + output);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);
        }

        private void ValidateSelection(CaptureCase captureCase, List<string> errors)
        {
            if (controller.SelectedTabIndex != captureCase.Tab)
            {
                errors.Add("selected tab does not match capture request");
            }
            for (var i = 0; i < controller.NativePages.Count; i++)
            {
                var expected = captureCase.Tab == i;
                if (controller.NativePages[i].activeSelf != expected)
                {
                    errors.Add("native page visibility mismatch: " + controller.NativePages[i].name);
                }
            }
            if (controller.Toolbox.Root.activeSelf != captureCase.Toolbox)
            {
                errors.Add("toolbox visibility mismatch");
            }
        }

        private static CaptureMetrics Capture(SettingsPreviewController controller, string path, int width, int height)
        {
            var camera = controller.PreviewCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                Canvas.ForceUpdateCanvases();
                controller.RefreshResponsiveLayout();
                camera.Render();
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return Analyze(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                controller.RefreshResponsiveLayout();
                Destroy(renderTexture);
                Destroy(texture);
            }
        }

        private static CaptureMetrics Analyze(Texture2D texture)
        {
            var nonBackground = 0;
            var probePixels = 0;
            var edgeAccentPixels = 0;
            var pixels = texture.GetPixels32();
            var step = Mathf.Max(1, pixels.Length / 240000);
            var stage = (Color32)PreviewTheme.Stage;
            for (var i = 0; i < pixels.Length; i += step)
            {
                var pixel = pixels[i];
                var distance = Mathf.Abs(pixel.r - stage.r) + Mathf.Abs(pixel.g - stage.g) + Mathf.Abs(pixel.b - stage.b);
                if (distance > 12) nonBackground++;
                if (pixel.r > 210 && pixel.g < 100 && pixel.b > 160) probePixels++;
            }
            var accent = (Color32)PreviewTheme.Accent;
            for (var x = 0; x < texture.width; x++)
            {
                if (NearAccent(pixels[x], accent)) edgeAccentPixels++;
                if (NearAccent(pixels[(texture.height - 1) * texture.width + x], accent)) edgeAccentPixels++;
            }
            for (var y = 0; y < texture.height; y++)
            {
                if (NearAccent(pixels[y * texture.width], accent)) edgeAccentPixels++;
                if (NearAccent(pixels[y * texture.width + texture.width - 1], accent)) edgeAccentPixels++;
            }
            return new CaptureMetrics
            {
                NonBackgroundSamples = nonBackground,
                ProbePixels = probePixels,
                EdgeAccentPixels = edgeAccentPixels
            };
        }

        private static bool NearAccent(Color32 pixel, Color32 accent)
        {
            return Mathf.Abs(pixel.r - accent.r) <= 8
                   && Mathf.Abs(pixel.g - accent.g) <= 8
                   && Mathf.Abs(pixel.b - accent.b) <= 8;
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
        }

        private static CaptureCase Case(
            string name,
            int tab,
            string scenario,
            int width,
            int height,
            bool toolbox = false,
            bool overlay = false,
            string category = "all",
            string overlayKind = "")
        {
            return new CaptureCase
            {
                Name = name,
                Tab = tab,
                Scenario = scenario,
                Width = width,
                Height = height,
                Toolbox = toolbox,
                Overlay = overlay,
                OverlayKind = overlayKind,
                Category = category
            };
        }

        private static bool HasArgument(string expected)
        {
            return Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
        }

        private static string ArgumentValue(string prefix)
        {
            var argument = Environment.GetCommandLineArgs()
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return argument == null ? "" : argument.Substring(prefix.Length).Trim('"');
        }

        private struct CaptureMetrics
        {
            internal int NonBackgroundSamples;
            internal int ProbePixels;
            internal int EdgeAccentPixels;
        }
    }
}
