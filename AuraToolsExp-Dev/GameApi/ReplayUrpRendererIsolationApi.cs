using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Gives replay cameras a process-owned URP renderer instance. The renderer data is
/// cloned from the game's renderer so shader/resource contracts, render passes, and
/// native UI materials stay intact. Native renderer slots and features are never
/// toggled or rewritten; replay isolation comes from its camera, layer, target, and
/// disabled gameplay behaviours rather than deleting render-pipeline capabilities.
/// </summary>
internal static class ReplayUrpRendererIsolationApi
{
    private const string DedicatedRendererDataName = "AuraToolsReplayNativeRendererDataV17";
    private const string AdditionalCameraDataTypeName =
        "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime";

    private static readonly object Gate = new();
    private static readonly Dictionary<int, RendererRegistration> Registrations = new();

    internal static ReplayUrpRendererIsolationLease Acquire(Camera camera)
    {
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        if (camera.gameObject == null)
            throw new InvalidOperationException("Replay camera GameObject is unavailable.");
        if (camera.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Replay renderer isolation must be assigned before camera activation.");

        var pipelineAsset = GraphicsSettings.currentRenderPipeline
                            ?? throw new InvalidOperationException("The active game render pipeline is unavailable.");
        if (!string.Equals(
                pipelineAsset.GetType().FullName,
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Replay rendering requires the active UniversalRenderPipelineAsset, actual="
                + pipelineAsset.GetType().FullName + ".");

        lock (Gate)
        {
            var assetId = pipelineAsset.GetInstanceID();
            if (!Registrations.TryGetValue(assetId, out var registration)
                || !ReferenceEquals(registration.Asset, pipelineAsset))
            {
                registration = RendererRegistration.DiscoverOrCreate(pipelineAsset);
                Registrations[assetId] = registration;
            }

            var renderer = registration.ResolveRenderer();
            var additionalType = Type.GetType(AdditionalCameraDataTypeName, throwOnError: false)
                                 ?? throw new MissingMemberException(
                                     "Unity URP additional camera data type is unavailable.");
            if (camera.gameObject.GetComponent(additionalType) != null)
                throw new InvalidOperationException(
                    "Replay camera already has URP additional data before renderer isolation.");

            var additional = camera.gameObject.AddComponent(additionalType);
            ReplayRendererCameraLeaseTokenV17 ownershipToken = default;
            try
            {
                SetOptionalBoolean(additional, "renderPostProcessing", false);
                SetOptionalBoolean(additional, "renderShadows", false);
                SetOptionalBoolean(additional, "requiresColorTexture", false);
                SetOptionalBoolean(additional, "requiresDepthTexture", false);
                SetOptionalBoolean(additional, "allowXRRendering", false);
                InvokeRequired(additional, "SetRenderer", new[] { typeof(int) }, registration.Slot);
                var assignedRenderer = GetRequiredProperty(additional, "scriptableRenderer");
                if (!ReferenceEquals(renderer, assignedRenderer))
                    throw new InvalidOperationException(
                        "URP rejected the AuraTools replay renderer slot assignment.");

                ownershipToken = registration.Acquire(camera.GetInstanceID());
                AuraToolsLog.Info("[MatchRecords] replay URP renderer assigned: asset="
                                  + pipelineAsset.name
                                  + ", camera=" + camera.GetInstanceID()
                                  + ", slot=" + registration.Slot
                                  + ", data=" + registration.Data.GetType().Name
                                  + ", renderer=" + renderer.GetType().Name
                                   + ", feature-profile=" + registration.FeatureSummary
                                   + ", native-renderers-unchanged=true.");
                return new ReplayUrpRendererIsolationLease(
                    camera,
                    additional,
                    registration,
                    additionalType,
                    ownershipToken);
            }
            catch (Exception assignmentFailure)
            {
                var rollbackFailures = new List<Exception>();
                try { InvokeRequired(additional, "SetRenderer", new[] { typeof(int) }, -1); }
                catch (Exception ex) { rollbackFailures.Add(ex); }
                if (ownershipToken.IsValid)
                {
                    var release = registration.Release(ownershipToken);
                    if (release != ReplayRendererCameraReleaseV17.Released)
                        rollbackFailures.Add(new InvalidOperationException(
                            "Replay renderer assignment rollback rejected ownership release: "
                            + release + "."));
                }
                Object.Destroy(additional);
                if (rollbackFailures.Count > 0)
                    throw new AggregateException(
                        "Replay renderer assignment and rollback both failed.",
                        new[] { assignmentFailure }.Concat(rollbackFailures));
                throw;
            }
        }
    }

    private static void SetOptionalBoolean(object target, string propertyName, bool value)
    {
        var property = FindProperty(target.GetType(), propertyName);
        if (property == null || !property.CanWrite || property.PropertyType != typeof(bool)) return;
        property.SetValue(target, value, null);
    }

    private static object? GetRequiredProperty(object target, string propertyName)
    {
        var property = FindProperty(target.GetType(), propertyName)
                       ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return property.GetValue(target, null);
    }

    private static object? InvokeRequired(
        object target,
        string methodName,
        Type[] parameterTypes,
        params object[] arguments)
    {
        var method = FindMethod(target.GetType(), methodName, parameterTypes)
                     ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                target.GetType().Name + "." + methodName + " failed: " + ex.InnerException.Message,
                ex.InnerException);
        }
    }

    private static FieldInfo RequireField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(type.FullName, fieldName);
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string methodName, Type[] parameterTypes)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var method = current.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: parameterTypes,
                modifiers: null);
            if (method != null) return method;
        }
        return null;
    }

    private static Array Append(Array source, object value)
    {
        var elementType = source.GetType().GetElementType()
                          ?? throw new InvalidOperationException("URP renderer array element type is unavailable.");
        var result = Array.CreateInstance(elementType, source.Length + 1);
        Array.Copy(source, result, source.Length);
        result.SetValue(value, source.Length);
        return result;
    }

    internal sealed class RendererRegistration
    {
        private readonly FieldInfo dataListField;
        private readonly FieldInfo renderersField;
        private readonly MethodInfo getRendererMethod;
        private readonly ReplayRendererIsolationContractV17 ownership = new();

        private RendererRegistration(
            RenderPipelineAsset asset,
            Object data,
            int slot,
            FieldInfo dataListField,
            FieldInfo renderersField,
            MethodInfo getRendererMethod,
            ReplayRendererFeatureProfile featureProfile)
        {
            Asset = asset;
            Data = data;
            Slot = slot;
            FeatureProfile = featureProfile;
            FeatureSummary = featureProfile.Summary;
            this.dataListField = dataListField;
            this.renderersField = renderersField;
            this.getRendererMethod = getRendererMethod;
        }

        internal RenderPipelineAsset Asset { get; }
        internal Object Data { get; }
        internal int Slot { get; }
        internal ReplayRendererFeatureProfile FeatureProfile { get; }
        internal string FeatureSummary { get; }

        internal static RendererRegistration DiscoverOrCreate(RenderPipelineAsset asset)
        {
            var assetType = asset.GetType();
            var dataField = RequireField(assetType, "m_RendererDataList");
            var renderersField = RequireField(assetType, "m_Renderers");
            var getRenderer = FindMethod(assetType, "GetRenderer", new[] { typeof(int) })
                              ?? throw new MissingMethodException(assetType.FullName, "GetRenderer");
            var dataList = dataField.GetValue(asset) as Array
                           ?? throw new InvalidOperationException("URP renderer data list is unavailable.");
            var renderers = renderersField.GetValue(asset) as Array
                            ?? throw new InvalidOperationException("URP renderer instance list is unavailable.");
            if (dataList.Length == 0 || renderers.Length != dataList.Length)
                throw new InvalidOperationException(
                    "URP renderer data and instance lists are not initialized consistently.");

            var defaultIndexField = FindField(assetType, "m_DefaultRendererIndex");
            var defaultIndex = defaultIndexField?.GetValue(asset) is int configured ? configured : 0;
            if (defaultIndex < 0 || defaultIndex >= dataList.Length)
                defaultIndex = 0;
            var sourceData = dataList.GetValue(defaultIndex) as Object
                             ?? throw new InvalidOperationException("URP default renderer data is unavailable.");

            var ownedSlots = Enumerable.Range(0, dataList.Length)
                .Where(index => dataList.GetValue(index) is Object value
                                && string.Equals(value.name, DedicatedRendererDataName, StringComparison.Ordinal))
                .ToArray();
            if (ownedSlots.Length > 1)
                throw new InvalidOperationException("Multiple AuraTools replay renderer slots were discovered.");
            if (ownedSlots.Length == 1)
            {
                var existing = (Object)dataList.GetValue(ownedSlots[0])!;
                var reusedProfile = ValidateExistingFeatureProfile(sourceData, existing);
                var reused = new RendererRegistration(
                    asset,
                    existing,
                    ownedSlots[0],
                    dataField,
                    renderersField,
                    getRenderer,
                    reusedProfile);
                reused.ResolveRenderer();
                AuraToolsLog.Info("[MatchRecords] replay URP renderer registration reused: asset="
                                   + asset.name + ", slot=" + reused.Slot
                                   + ", feature-profile=" + reused.FeatureSummary + ".");
                return reused;
            }

            var clonedData = Object.Instantiate(sourceData);
            clonedData.name = DedicatedRendererDataName;
            clonedData.hideFlags = HideFlags.DontSave;
            var ownedFeatures = new List<Object>();
            object? renderer = null;
            var committed = false;
            try
            {
                var featureProfile = CreateOwnedFeatureProfile(sourceData, clonedData, ownedFeatures);
                var create = FindMethod(clonedData.GetType(), "InternalCreateRenderer", Type.EmptyTypes)
                             ?? throw new MissingMethodException(
                                 clonedData.GetType().FullName,
                                 "InternalCreateRenderer");
                try { renderer = create.Invoke(clonedData, null); }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw new InvalidOperationException(
                        "URP dedicated replay renderer creation failed: " + ex.InnerException.Message,
                        ex.InnerException);
                }
                if (renderer == null)
                    throw new InvalidOperationException("URP dedicated replay renderer creation returned null.");

                var nextDataList = Append(dataList, clonedData);
                var nextRenderers = Append(renderers, renderer);
                try
                {
                    dataField.SetValue(asset, nextDataList);
                    renderersField.SetValue(asset, nextRenderers);
                    committed = true;
                }
                catch
                {
                    dataField.SetValue(asset, dataList);
                    renderersField.SetValue(asset, renderers);
                    throw;
                }

                var registration = new RendererRegistration(
                    asset,
                    clonedData,
                    dataList.Length,
                    dataField,
                    renderersField,
                    getRenderer,
                    featureProfile);
                registration.ResolveRenderer();
                AuraToolsLog.Info("[MatchRecords] replay URP renderer registered: asset="
                                  + asset.name
                                  + ", slot=" + registration.Slot
                                  + ", source=" + sourceData.GetType().Name
                                   + ", feature-profile=" + registration.FeatureSummary
                                   + ", native-renderers-unchanged=true.");
                return registration;
            }
            catch
            {
                if (committed)
                {
                    dataField.SetValue(asset, dataList);
                    renderersField.SetValue(asset, renderers);
                }
                if (renderer != null)
                {
                    var dispose = FindMethod(renderer.GetType(), "Dispose", Type.EmptyTypes);
                    try { dispose?.Invoke(renderer, null); }
                    catch (Exception cleanupError)
                    {
                        AuraToolsLog.Error(
                            "[MatchRecords] failed replay URP renderer registration cleanup failed",
                            cleanupError);
                    }
                }
                else
                {
                    foreach (var feature in ownedFeatures.OfType<ScriptableRendererFeature>())
                    {
                        try { feature.Dispose(); }
                        catch (Exception cleanupError)
                        {
                            AuraToolsLog.Error(
                                "[MatchRecords] failed replay URP feature cleanup failed",
                                cleanupError);
                        }
                    }
                }
                foreach (var feature in ownedFeatures)
                    if (feature != null) Object.Destroy(feature);
                Object.Destroy(clonedData);
                throw;
            }
        }

        internal object ResolveRenderer()
        {
            var dataList = dataListField.GetValue(Asset) as Array
                           ?? throw new InvalidOperationException("URP renderer data list was replaced.");
            var renderers = renderersField.GetValue(Asset) as Array
                            ?? throw new InvalidOperationException("URP renderer instance list was replaced.");
            if (Slot < 0 || Slot >= dataList.Length || Slot >= renderers.Length
                || !ReferenceEquals(dataList.GetValue(Slot), Data))
                throw new InvalidOperationException(
                    "AuraTools replay renderer slot was externally changed or invalidated.");
            object? resolved;
            try { resolved = getRendererMethod.Invoke(Asset, new object[] { Slot }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException(
                    "URP could not resolve the AuraTools replay renderer: " + ex.InnerException.Message,
                    ex.InnerException);
            }
            var currentRenderers = renderersField.GetValue(Asset) as Array
                                   ?? throw new InvalidOperationException("URP renderer instance list disappeared.");
            if (resolved == null || Slot >= currentRenderers.Length
                                 || !ReferenceEquals(currentRenderers.GetValue(Slot), resolved))
                throw new InvalidOperationException(
                    "URP resolved a different renderer for the AuraTools replay slot.");
            return resolved;
        }

        internal ReplayRendererCameraLeaseTokenV17 Acquire(int cameraId) => ownership.Acquire(cameraId);

        internal bool ValidateLease(ReplayRendererCameraLeaseTokenV17 token, int cameraId) =>
            ownership.Validate(token, cameraId);

        internal ReplayRendererCameraReleaseV17 Release(ReplayRendererCameraLeaseTokenV17 token) =>
            ownership.Release(token);

        private static ReplayRendererFeatureProfile CreateOwnedFeatureProfile(
            Object sourceData,
            Object clonedData,
            ICollection<Object> ownedFeatures)
        {
            var sourceFeatures = FeatureList(sourceData);
            var targetFeatures = FeatureList(clonedData);
            if (ReferenceEquals(sourceFeatures, targetFeatures))
                throw new InvalidOperationException(
                    "Cloned URP renderer data shares the native renderer feature list.");
            var sourceFeatureMap = RequireField(sourceData.GetType(), "m_RendererFeatureMap").GetValue(sourceData) as IList
                                   ?? throw new InvalidOperationException("Native URP renderer feature map is unavailable.");
            var featureMap = RequireField(clonedData.GetType(), "m_RendererFeatureMap").GetValue(clonedData) as IList
                             ?? throw new InvalidOperationException("URP renderer feature map is unavailable.");
            if (ReferenceEquals(sourceFeatureMap, featureMap))
                throw new InvalidOperationException(
                    "Cloned URP renderer data shares the native renderer feature map.");
            var decisions = Decisions(sourceFeatures);
            targetFeatures.Clear();
            featureMap.Clear();

            foreach (var item in decisions.Where(item =>
                         item.Decision.Disposition == ReplayRendererFeatureDispositionV17.RetainOwnedClone))
            {
                ValidateRetainedFeature(item.Feature);
                var clone = Object.Instantiate(item.Feature);
                clone.name = "AuraToolsReplayOwned:" + item.Feature.name;
                clone.hideFlags = HideFlags.DontSave;
                targetFeatures.Add(clone);
                featureMap.Add(0L);
                ownedFeatures.Add(clone);
            }

            if (decisions.Any(item => item.Decision.RequiresIntermediateColor))
            {
                var intermediate = ScriptableObject.CreateInstance<ReplayIntermediateColorRendererFeatureV17>();
                intermediate.name = "AuraToolsReplayIntermediateColorV17";
                intermediate.hideFlags = HideFlags.DontSave;
                targetFeatures.Insert(0, intermediate);
                featureMap.Insert(0, 0L);
                ownedFeatures.Add(intermediate);
            }

            if (targetFeatures.Count != featureMap.Count)
                throw new InvalidOperationException(
                    "Replay renderer feature list and feature map are not aligned.");
            return Profile(decisions, targetFeatures, ownership: "deep-cloned");
        }

        private static ReplayRendererFeatureProfile ValidateExistingFeatureProfile(
            Object sourceData,
            Object existingData)
        {
            var sourceFeatures = FeatureList(sourceData);
            var existingFeatures = FeatureList(existingData);
            var existingFeatureMap = RequireField(existingData.GetType(), "m_RendererFeatureMap").GetValue(existingData) as IList
                                     ?? throw new InvalidOperationException(
                                         "Existing replay renderer feature map is unavailable.");
            if (existingFeatureMap.Count != existingFeatures.Count)
                throw new InvalidOperationException(
                    "Existing replay renderer feature list and feature map are not aligned.");
            var decisions = Decisions(sourceFeatures);
            var expected = decisions
                .Where(item => item.Decision.Disposition == ReplayRendererFeatureDispositionV17.RetainOwnedClone)
                .Select(item => item.Feature.GetType().FullName ?? item.Feature.GetType().Name)
                .ToList();
            if (decisions.Any(item => item.Decision.RequiresIntermediateColor))
                expected.Insert(0, typeof(ReplayIntermediateColorRendererFeatureV17).FullName!);
            var actual = existingFeatures.Cast<object?>().Select(item =>
            {
                if (item is not ScriptableRendererFeature feature)
                    throw new InvalidOperationException(
                        "Existing replay renderer contains a missing or invalid renderer feature.");
                if (sourceFeatures.Cast<object?>().Any(source => ReferenceEquals(source, feature)))
                    throw new InvalidOperationException(
                        "Existing replay renderer shares a native renderer feature instance.");
                ValidateRetainedFeatureIfApplicable(feature);
                return feature.GetType().FullName ?? feature.GetType().Name;
            }).ToList();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    "Existing replay renderer feature profile is incompatible: expected="
                    + Format(expected) + ", actual=" + Format(actual) + ".");
            return Profile(decisions, existingFeatures, ownership: "reused-owned");
        }

        private static IList FeatureList(Object rendererData)
        {
            var featuresProperty = FindProperty(rendererData.GetType(), "rendererFeatures")
                                   ?? throw new MissingMemberException(
                                       rendererData.GetType().FullName,
                                       "rendererFeatures");
            return featuresProperty.GetValue(rendererData, null) as IList
                   ?? throw new InvalidOperationException("URP renderer feature list is unavailable.");
        }

        private static List<FeatureDecision> Decisions(IList sourceFeatures)
        {
            var result = new List<FeatureDecision>(sourceFeatures.Count);
            foreach (var value in sourceFeatures.Cast<object?>())
            {
                if (value is not ScriptableRendererFeature feature)
                    throw new InvalidOperationException("Native URP renderer contains a missing renderer feature.");
                var typeName = feature.GetType().FullName ?? feature.GetType().Name;
                var decision = ReplayRendererFeaturePolicyV17.Decide(typeName, feature.isActive);
                if (decision.Disposition == ReplayRendererFeatureDispositionV17.RejectProfile)
                    throw new InvalidOperationException(
                        "Replay renderer feature profile is unsupported: type=" + typeName
                        + ", reason=" + decision.Reason + ".");
                result.Add(new FeatureDecision(feature, decision));
            }
            return result;
        }

        private static void ValidateRetainedFeatureIfApplicable(ScriptableRendererFeature feature)
        {
            if (string.Equals(
                    feature.GetType().FullName,
                    ReplayRendererFeaturePolicyV17.FullScreenPassRendererFeature,
                    StringComparison.Ordinal))
                ValidateRetainedFeature(feature);
        }

        private static void ValidateRetainedFeature(ScriptableRendererFeature feature)
        {
            if (feature is not FullScreenPassRendererFeature fullScreen)
                throw new InvalidOperationException(
                    "Replay retained renderer feature has no runtime validator: "
                    + (feature.GetType().FullName ?? feature.GetType().Name) + ".");
            if (fullScreen.passMaterial == null)
                throw new InvalidOperationException(
                    "Replay full-screen renderer feature has no material.");
            if (fullScreen.passIndex < 0 || fullScreen.passIndex >= fullScreen.passMaterial.passCount)
                throw new InvalidOperationException(
                    "Replay full-screen renderer feature pass index is invalid: "
                    + fullScreen.passIndex + "/" + fullScreen.passMaterial.passCount + ".");
            if (fullScreen.requirements != ScriptableRenderPassInput.None)
                throw new InvalidOperationException(
                    "Replay full-screen renderer feature declares unsupported auxiliary inputs: "
                    + fullScreen.requirements + ".");
        }

        private static ReplayRendererFeatureProfile Profile(
            IReadOnlyCollection<FeatureDecision> decisions,
            IList retainedFeatures,
            string ownership)
        {
            var source = decisions.Select(item =>
                item.Feature.GetType().Name + "(" + (item.Decision.SourceActive ? "active" : "inactive") + ")");
            var retained = retainedFeatures.Cast<object?>().Select(DescribeRetainedFeature);
            var excluded = decisions
                .Where(item => item.Decision.Disposition == ReplayRendererFeatureDispositionV17.ExcludeFromReplay)
                .Select(item => item.Feature.GetType().Name + ":" + item.Decision.Reason);
            return new ReplayRendererFeatureProfile(
                Format(source),
                Format(retained),
                Format(excluded),
                ownership);
        }

        private static string DescribeRetainedFeature(object? value)
        {
            if (value is FullScreenPassRendererFeature fullScreen)
                return fullScreen.GetType().Name
                       + "(fetchColor=" + fullScreen.fetchColorBuffer
                       + ",requirements=" + fullScreen.requirements
                       + ",pass=" + fullScreen.passIndex
                       + ",shader=" + (fullScreen.passMaterial?.shader?.name ?? "<missing>") + ")";
            return value?.GetType().Name ?? "<missing>";
        }

        private static string Format(IEnumerable<string> values)
        {
            var materialized = values.ToArray();
            return materialized.Length == 0 ? "none" : "[" + string.Join("|", materialized) + "]";
        }

        private sealed class FeatureDecision
        {
            internal FeatureDecision(
                ScriptableRendererFeature feature,
                ReplayRendererFeatureDecisionV17 decision)
            {
                Feature = feature;
                Decision = decision;
            }

            internal ScriptableRendererFeature Feature { get; }
            internal ReplayRendererFeatureDecisionV17 Decision { get; }
        }
    }

    internal sealed class ReplayRendererFeatureProfile
    {
        internal ReplayRendererFeatureProfile(
            string source,
            string retained,
            string excluded,
            string ownership)
        {
            Source = source;
            Retained = retained;
            Excluded = excluded;
            Ownership = ownership;
        }

        internal string Source { get; }
        internal string Retained { get; }
        internal string Excluded { get; }
        internal string Ownership { get; }
        internal string Summary => "source=" + Source
                                   + ", retained=" + Retained
                                   + ", excluded=" + Excluded
                                   + ", ownership=" + Ownership;
    }

    internal sealed class ReplayUrpRendererIsolationLease : IDisposable
    {
        private Camera? camera;
        private Object? additionalData;
        private RendererRegistration? registration;
        private readonly Type additionalType;
        private readonly PropertyInfo scriptableRendererProperty;
        private readonly ReplayRendererCameraLeaseTokenV17 ownershipToken;

        internal ReplayUrpRendererIsolationLease(
            Camera camera,
            Object additionalData,
            RendererRegistration registration,
            Type additionalType,
            ReplayRendererCameraLeaseTokenV17 ownershipToken)
        {
            this.camera = camera;
            this.additionalData = additionalData;
            this.registration = registration;
            this.additionalType = additionalType;
            this.ownershipToken = ownershipToken;
            scriptableRendererProperty = FindProperty(additionalType, "scriptableRenderer")
                                         ?? throw new MissingMemberException(
                                             additionalType.FullName,
                                             "scriptableRenderer");
        }

        internal int RendererSlot => registration?.Slot ?? -1;

        internal void Validate(Camera expectedCamera)
        {
            var currentRegistration = registration
                                      ?? throw new ObjectDisposedException(
                                          nameof(ReplayUrpRendererIsolationLease));
            if (camera == null || !ReferenceEquals(camera, expectedCamera)
                               || additionalData == null
                               || camera.gameObject == null
                               || !ReferenceEquals(camera.gameObject.GetComponent(additionalType), additionalData))
                throw new InvalidOperationException(
                    "Replay camera URP renderer ownership was externally changed.");
            if (!currentRegistration.ValidateLease(ownershipToken, camera.GetInstanceID()))
                throw new InvalidOperationException(
                    "Replay camera no longer owns the dedicated URP renderer lease.");
            var expectedRenderer = currentRegistration.ResolveRenderer();
            var assignedRenderer = scriptableRendererProperty.GetValue(additionalData, null);
            if (!ReferenceEquals(expectedRenderer, assignedRenderer))
                throw new InvalidOperationException(
                    "Replay camera no longer points at the AuraTools dedicated URP renderer.");
        }

        public void Dispose()
        {
            var currentRegistration = registration;
            if (currentRegistration == null) return;
            registration = null;
            Exception? failure = null;
            try
            {
                if (additionalData != null)
                    InvokeRequired(additionalData, "SetRenderer", new[] { typeof(int) }, -1);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (additionalData != null) Object.Destroy(additionalData);
                additionalData = null;
                var release = currentRegistration.Release(ownershipToken);
                if (release != ReplayRendererCameraReleaseV17.Released)
                    failure ??= new InvalidOperationException(
                        "Replay URP renderer ownership release was rejected: " + release + ".");
                AuraToolsLog.Info("[MatchRecords] replay URP renderer released: camera="
                                  + (camera == null ? "destroyed" : camera.GetInstanceID().ToString())
                                  + ", slot=" + currentRegistration.Slot
                                  + ", additional-data=" + additionalType.Name + ".");
                camera = null;
            }
            if (failure != null)
                throw new InvalidOperationException(
                    "Replay URP camera renderer assignment could not be reset.",
                    failure);
        }
    }
}
