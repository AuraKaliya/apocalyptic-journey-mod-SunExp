using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraCg.Shared;
using AuraToolsExp.Dll.Features.Cg;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EventCgPreview
{
    public sealed class PreviewBootstrap : MonoBehaviour
    {
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly List<object> results = new List<object>();
        private string artRoot;
        private string output;
        private AuraToolsEventCgArtCatalog catalog;
        private bool failed;
        private readonly HashSet<string> captureHashes = new HashSet<string>();
        private Camera captureCamera;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["caroline"] = "卡洛琳", ["coco"] = "可可", ["amelia"] = "阿米莉娅", ["wuna"] = "乌娜",
            ["hermia"] = "厄米娅", ["caroline-alt"] = "异色卡洛琳", ["hermia-alt"] = "异色厄米娅",
            ["coco-alt"] = "异色可可", ["nana"] = "奈奈", ["adela"] = "阿黛拉", ["vivian"] = "薇薇安",
            ["husk"] = "失心躯壳", ["loneer"] = "洛奈尔", ["columbina"] = "哥伦比娅", ["olimya"] = "奥莉米娅"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            new GameObject("EventCgAcceptance").AddComponent<PreviewBootstrap>();
        }

        private void Awake()
        {
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            artRoot = Argument("-cgArtRoot=");
            output = Argument("-cgOutput=");
            Directory.CreateDirectory(output);
            catalog = AuraToolsEventCgArtCatalog.Parse(File.ReadAllText(Path.Combine(artRoot, "event-cg.art.json")));
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                failed = true;
                if (!string.IsNullOrWhiteSpace(output))
                    File.WriteAllText(Path.Combine(output, "failure.txt"), message + "\n" + trace);
                Application.Quit(1);
            }
        }

        private IEnumerator Start()
        {
            var canvasObject = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            captureCamera = new GameObject("CaptureCamera", typeof(Camera)).GetComponent<Camera>();
            captureCamera.transform.position = new Vector3(0f, 0f, -10f);
            captureCamera.orthographic = true;
            captureCamera.orthographicSize = 5f;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0.04f, 0.055f, 0.075f, 1f);
            captureCamera.nearClipPlane = 0.1f;
            captureCamera.farClipPlane = 100f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = captureCamera;
            canvas.planeDistance = 1f;
            var probe = new GameObject("UnderlyingInputProbe", typeof(RectTransform), typeof(Image));
            probe.transform.SetParent(canvasObject.transform, false);
            var probeRect = probe.GetComponent<RectTransform>();
            probeRect.anchorMin = Vector2.zero; probeRect.anchorMax = Vector2.one;
            probeRect.offsetMin = probeRect.offsetMax = Vector2.zero;
            probe.GetComponent<Image>().color = new Color(0.04f, 0.055f, 0.075f, 1f);
            new GameObject("EventSystem", typeof(EventSystem));
            var renderer = new AuraCgSceneCompositionRenderer(canvasObject.transform, "ProductionPosterRenderer");
            var cases = new List<Tuple<string, int, int, int, int>>();
            for (var count = 1; count <= 8; count++) cases.Add(Tuple.Create("victory.standard", count, 1280, 720, 0));
            var offset = 0;
            foreach (var theme in catalog.Themes.Keys.Where(key => key != "victory.standard"))
                cases.Add(Tuple.Create(theme, 4, 1280, 720, offset++ * 2));
            cases.Add(Tuple.Create("victory.standard", 8, 922, 838, 0));
            cases.Add(Tuple.Create("victory.standard", 4, 922, 838, 0));
            cases.Add(Tuple.Create("victory.ritual", 1, 1280, 720, 1));
            cases.Add(Tuple.Create("battle-defeat", 1, 1280, 720, 0));
            cases.Add(Tuple.Create("adventure-settlement", 4, 1280, 720, 12));
            cases.Add(Tuple.Create("victory.standard", 4, 520, 292, 0));
            foreach (var item in cases)
            {
                Screen.SetResolution(item.Item3, item.Item4, FullScreenMode.Windowed);
                for (var frame = 0; frame < 4; frame++) yield return null;
                captureCamera.ResetAspect();
                Canvas.ForceUpdateCanvases();
                var plan = Plan(item.Item1, item.Item2, item.Item5);
                using (var presentation = Presentation(plan))
                {
                    if (!renderer.Bind(presentation, plan, 5f)) throw new InvalidOperationException("Production renderer did not bind.");
                    renderer.UpdateFrames(1.5f);
                    yield return new WaitForEndOfFrame();
                    var name = item.Item1 + "-" + item.Item2 + "p-" + Screen.width + "x" + Screen.height
                        + (item.Item5 == 0 ? "" : "-group" + item.Item5);
                    var hash = Capture(canvas, renderer, Path.Combine(output, name + ".png"));
                    CheckFaces(canvas, presentation);
                    var graphics = canvasObject.GetComponentsInChildren<Graphic>(true);
                    if (graphics.Any(graphic => graphic.gameObject != probe && graphic.raycastTarget))
                        throw new InvalidOperationException("Poster graphic captures pointer input.");
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) }, hits);
                    if (hits.Count == 0 || hits[0].gameObject != probe) throw new InvalidOperationException("Underlying UI is not reachable.");
                    renderer.Hide();
                    if (canvasObject.GetComponentsInChildren<Image>(true).Any(view => view.name.StartsWith("Artwork.") && view.sprite != null))
                        throw new InvalidOperationException("Hidden pooled artwork retains a sprite.");
                    if (!renderer.Bind(presentation, plan, 5f)) throw new InvalidOperationException("Poster cannot be reused after Hide.");
                    plan.MotionEnabled = false;
                    renderer.UpdateFrames(3f);
                    if (canvasObject.transform.Find("ProductionPosterRenderer/PosterCanvas/PosterMotion").localScale != Vector3.one)
                        throw new InvalidOperationException("Reduced motion still moves the camera.");
                    renderer.Hide();
                    results.Add(new { name, participants = item.Item2, width = Screen.width, height = Screen.height, sha256 = hash, nonBlankPass = true, faceVisibilityPass = true, raycastPass = true, reusePass = true, reducedMotionPass = true });
                }
            }
            renderer.Dispose();
            yield return null;
            var temporaryHost = new GameObject("DestroyedPreviewHost", typeof(RectTransform));
            temporaryHost.transform.SetParent(canvasObject.transform, false);
            var orphan = new AuraCgSceneCompositionRenderer(temporaryHost.transform, "DestroyedHostPoster");
            Destroy(temporaryHost);
            yield return null;
            orphan.Hide();
            orphan.Dispose();
            File.WriteAllText(Path.Combine(output, "report.json"), JsonConvert.SerializeObject(new { success = !failed, cases = results, destroyedHostDisposePass = true, source = "production CG renderer and planner; font lookup adapted for standalone Unity" }, Formatting.Indented));
            Application.Quit(failed ? 1 : 0);
        }

        private AuraCgScenePlan Plan(string scene, int count, int offset)
        {
            var source = new AuraCgSceneSourceSnapshot { SceneId = scene, EventToken = "unity-acceptance" };
            var roles = catalog.PreviewRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            for (var index = 0; index < count; index++)
                source.Participants.Add(new AuraCgSceneParticipantSource { Order = index, PlayerId = "p" + index, RoleId = roles[(offset + index) % roles.Length], RoleLayerAsset = new AuraCgSceneAssetReference { OwnerModId = "AuraToolsExp", AssetId = "portrait" } });
            return AuraCgTeamScenePlanner.Build(source, new AuraCgSceneTemplateSpec { BackgroundAsset = new AuraCgSceneAssetReference { OwnerModId = "AuraToolsExp", AssetId = "background" }, PresentationProfileId = scene }, "aura.battle.victory")!;
        }

        private string Capture(Canvas canvas, AuraCgSceneCompositionRenderer renderer, string path)
        {
            var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = captureCamera;
                canvas.planeDistance = 1f;
                captureCamera.targetTexture = target;
                captureCamera.ResetAspect();
                RenderTexture.active = target;
                Canvas.ForceUpdateCanvases();
                renderer.UpdateFrames(1.5f);
                captureCamera.Render();
                texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                texture.Apply();
                var pixels = texture.GetPixels32();
                var colors = new HashSet<int>();
                for (var index = 0; index < pixels.Length; index += 17)
                {
                    var color = pixels[index];
                    colors.Add((color.r << 16) | (color.g << 8) | color.b);
                }
                if (colors.Count < 200) throw new InvalidOperationException("Capture contains no meaningful poster pixels.");
                var bytes = texture.EncodeToPNG();
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                if (!captureHashes.Add(hash)) throw new InvalidOperationException("Two different poster cases produced identical images.");
                File.WriteAllBytes(path, bytes);
                return hash;
            }
            finally
            {
                captureCamera.targetTexture = null;
                RenderTexture.active = previous;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = captureCamera;
                Canvas.ForceUpdateCanvases();
                renderer.UpdateFrames(1.5f);
                Destroy(target);
                Destroy(texture);
            }
        }

        private void CheckFaces(Canvas canvas, AuraCgScenePresentation presentation)
        {
            var views = canvas.GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("Artwork.Role.")).ToArray();
            foreach (var role in presentation.Participants)
            {
                var view = views.Single(image => image.name == "Artwork.Role." + role.Plan.SeatIndex);
                var rectangle = view.rectTransform.rect;
                var local = new Vector3(rectangle.xMin + rectangle.width * role.Portrait.FaceX,
                    rectangle.yMax - rectangle.height * role.Portrait.FaceY, 0f);
                var world = view.rectTransform.TransformPoint(local);
                var screen = RectTransformUtility.WorldToScreenPoint(captureCamera, world);
                if (screen.x < 0 || screen.x >= Screen.width || screen.y < 0 || screen.y >= Screen.height)
                    throw new InvalidOperationException("Face center leaves the canvas: " + role.Plan.RoleId);
                foreach (var mask in view.GetComponentsInParent<RectMask2D>())
                    if (mask.isActiveAndEnabled && !RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, screen, captureCamera))
                        throw new InvalidOperationException("Face center is clipped: " + role.Plan.RoleId);
                foreach (var other in views.Where(image => image.transform.parent.GetSiblingIndex() > view.transform.parent.GetSiblingIndex()))
                {
                    if (other.GetComponentsInParent<RectMask2D>().Any(mask => mask.isActiveAndEnabled
                        && !RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, screen, captureCamera))) continue;
                    var point = other.rectTransform.InverseTransformPoint(world);
                    var rect = other.rectTransform.rect;
                    if (!rect.Contains(point)) continue;
                    var u = (point.x - rect.xMin) / rect.width;
                    var v = (point.y - rect.yMin) / rect.height;
                    if (other.sprite.texture.GetPixelBilinear(u, v).a * other.color.a > 0.90f)
                        throw new InvalidOperationException("A portrait covers a neighboring face: " + role.Plan.RoleId);
                }
            }
        }

        private AuraCgScenePresentation Presentation(AuraCgScenePlan plan)
        {
            var theme = catalog.Themes[plan.SceneId];
            var result = new AuraCgScenePresentation { Background = Sprite(theme.Background), Artwork = new AuraCgSceneArtwork { DarkTitle = theme.DarkTitle, CameraPush = theme.CameraPush } };
            foreach (var companion in theme.Layers) result.SceneLayers.Add(Companion(companion));
            foreach (var participant in plan.Participants)
            {
                var character = catalog.FindCharacter(participant.RoleId)!;
                var key = catalog.ResolvePose(character, plan.SceneId);
                var asset = catalog.Assets[key];
                var sprite = Sprite(key);
                var layer = new AuraCgSceneLayerPresentation { Plan = participant, DisplayName = Names.TryGetValue(character.Id, out var displayName) ? displayName : character.Id, Frames = new[] { sprite }, Portrait = asset.Portrait,
                    CanvasWidth = sprite.rect.width, CanvasHeight = sprite.rect.height, VisibleBounds = Bounds(sprite), FrameSeconds = 1f, Loop = false };
                foreach (var companion in asset.Layers) layer.Attachments.Add(Companion(companion));
                result.Participants.Add(layer);
            }
            result.Ready = true;
            return result;
        }

        private AuraCgSceneArtLayerPresentation Companion(AuraToolsEventCgCompanionArt value) => new AuraCgSceneArtLayerPresentation
        {
            Frames = new[] { Sprite(value.Asset) }, FrameSeconds = 1f, Loop = false,
            Spec = new AuraCgSceneArtLayerSpec { Asset = new AuraCgSceneAssetReference { OwnerModId = "AuraToolsExp", AssetId = value.Asset }, Foreground = value.Foreground, Required = value.Required, Opacity = value.Opacity, MotionX = value.MotionX, MotionY = value.MotionY, Pulse = value.Pulse }
        };

        private Sprite Sprite(string id)
        {
            if (sprites.TryGetValue(id, out var sprite)) return sprite;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(AuraToolsEventCgArtCatalog.ResolveAssetPath(artRoot, catalog.Assets[id].Path)))) throw new InvalidDataException("Image decode failed: " + id);
            sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprites[id] = sprite;
            return sprite;
        }

        private static AuraCgNormalizedBounds Bounds(Sprite sprite)
        {
            var texture = sprite.texture;
            var pixels = texture.GetPixels32();
            var left = texture.width; var right = 0; var bottom = texture.height; var top = 0;
            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
                if (pixels[y * texture.width + x].a > 8) { left = Math.Min(left, x); right = Math.Max(right, x + 1); bottom = Math.Min(bottom, y); top = Math.Max(top, y + 1); }
            return new AuraCgNormalizedBounds(left / (float)texture.width, bottom / (float)texture.height, (right - left) / (float)texture.width, (top - bottom) / (float)texture.height);
        }

        private static string Argument(string prefix) => Environment.GetCommandLineArgs().First(arg => arg.StartsWith(prefix)).Substring(prefix.Length);
    }
}
