using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch;

namespace Terrias.Dll.GameApi;

public static class TruthCurrencyApi
{
    private const int DebitTokenLimit = 64;
    private const string CurrencyResourcePath = "Icon/UI_Icons/Native/Icon/真理之晶";
    private const string CurrencyFallbackResourcePath = "Icon/成就/真理之晶";
    private static readonly object Gate = new();

    public static int Balance()
    {
        try { return Math.Max(0, Singleton<GameRuntimeData>.Instance?.Truth ?? 0); }
        catch { return 0; }
    }

    public static bool TrySpend(int amount)
    {
        amount = Math.Max(0, amount);
        lock (Gate)
        {
            try
            {
                var runtime = Singleton<GameRuntimeData>.Instance;
                if (runtime == null || runtime.Truth < amount) return false;
                runtime.Truth -= amount;
                return true;
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[TruthCurrency] spend failed: " + ex.Message);
                return false;
            }
        }
    }

    public static void Refund(int amount)
    {
        if (amount <= 0) return;
        lock (Gate)
        {
            try
            {
                var runtime = Singleton<GameRuntimeData>.Instance;
                if (runtime != null) runtime.Truth += amount;
            }
            catch (Exception ex)
            {
                TerriasLog.Error("[TruthCurrency] refund failed", ex);
            }
        }
    }

    public static bool TrySpendAndRecord(int amount, string token, out string reason)
    {
        amount = Math.Max(0, amount);
        token = (token ?? "").Trim();
        if (amount <= 0 || token.Length == 0)
        {
            reason = "真理之晶扣除参数无效。";
            return false;
        }
        lock (Gate)
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            if (runtime == null)
            {
                reason = "当前账号货币档案尚未就绪。";
                return false;
            }
            if (HasDebitTokenUnlocked(runtime, token))
            {
                reason = "";
                return true;
            }
            if (runtime.Truth < amount)
            {
                reason = "真理之晶不足。";
                return false;
            }

            var previousBalance = Math.Max(0, (int)runtime.Truth);
            var variables = EnsureVariables(runtime);
            var hadPreviousTokenState = variables.TryGetValue(
                TerriasIds.SpiritArtifactTruthDebitTokensKey,
                out var previousTokenState);
            AddDebitTokenUnlocked(runtime, token);
            try
            {
                // Truth is the native account-level property. Its setter saves the
                // complete GameRuntimeData document, so the balance and token journal
                // cross the persistence boundary in the same write.
                runtime.Truth = previousBalance - amount;
                reason = "";
                return true;
            }
            catch (Exception ex)
            {
                RestoreTokenStateUnlocked(variables, hadPreviousTokenState, previousTokenState);
                try { runtime.Truth = previousBalance; }
                catch (Exception rollbackEx)
                {
                    TerriasLog.Error("[TruthCurrency] debit rollback persistence failed", rollbackEx);
                }
                TerriasLog.Warn("[TruthCurrency] account debit persistence failed: " + ex.Message);
                reason = "真理之晶扣除未能持久化，抽取已经回滚。";
                return false;
            }
        }
    }

    public static bool RefundAndRemoveRecord(int amount, string token)
    {
        token = (token ?? "").Trim();
        lock (Gate)
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            if (runtime == null || !HasDebitTokenUnlocked(runtime, token)) return false;
            var previousBalance = Math.Max(0, (int)runtime.Truth);
            var variables = EnsureVariables(runtime);
            var hadPreviousTokenState = variables.TryGetValue(
                TerriasIds.SpiritArtifactTruthDebitTokensKey,
                out var previousTokenState);
            RemoveDebitTokenUnlocked(runtime, token);
            try
            {
                var refund = Math.Max(0, amount);
                if (refund > 0) runtime.Truth = previousBalance + refund;
                else runtime.Save();
                return true;
            }
            catch (Exception ex)
            {
                RestoreTokenStateUnlocked(variables, hadPreviousTokenState, previousTokenState);
                try
                {
                    if ((int)runtime.Truth != previousBalance) runtime.Truth = previousBalance;
                    else runtime.Save();
                }
                catch (Exception rollbackEx)
                {
                    TerriasLog.Error("[TruthCurrency] refund rollback persistence failed", rollbackEx);
                }
                TerriasLog.Warn("[TruthCurrency] account refund persistence failed: " + ex.Message);
                return false;
            }
        }
    }

    public static bool HasDebitToken(string token)
    {
        lock (Gate)
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            return runtime != null && HasDebitTokenUnlocked(runtime, (token ?? "").Trim());
        }
    }

    public static Sprite? CurrencySprite()
    {
        try
        {
            return TerriasResourceCache.Load<Sprite>(CurrencyResourcePath, false, "truth.currency")
                   ?? TerriasResourceCache.Load<Sprite>(CurrencyFallbackResourcePath, false, "truth.currency.fallback");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[TruthCurrency] icon lookup failed: " + ex.Message);
            return null;
        }
    }

    private static bool HasDebitTokenUnlocked(GameRuntimeData runtime, string token)
        => DebitTokens(runtime).Contains(token ?? "", StringComparer.Ordinal);

    private static void AddDebitTokenUnlocked(GameRuntimeData runtime, string token)
    {
        var tokens = DebitTokens(runtime);
        if (!tokens.Contains(token, StringComparer.Ordinal)) tokens.Add(token);
        while (tokens.Count > DebitTokenLimit) tokens.RemoveAt(0);
        EnsureVariables(runtime)[TerriasIds.SpiritArtifactTruthDebitTokensKey] = string.Join("|", tokens);
    }

    private static void RemoveDebitTokenUnlocked(GameRuntimeData runtime, string token)
    {
        var tokens = DebitTokens(runtime);
        tokens.RemoveAll(value => string.Equals(value, token, StringComparison.Ordinal));
        var variables = EnsureVariables(runtime);
        if (tokens.Count == 0) variables.Remove(TerriasIds.SpiritArtifactTruthDebitTokensKey);
        else variables[TerriasIds.SpiritArtifactTruthDebitTokensKey] = string.Join("|", tokens);
    }

    private static List<string> DebitTokens(GameRuntimeData runtime)
    {
        if (!EnsureVariables(runtime).TryGetValue(TerriasIds.SpiritArtifactTruthDebitTokensKey, out var raw))
            return new List<string>();
        var values = (raw ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).ToList();
        return values.Skip(Math.Max(0, values.Count - DebitTokenLimit)).ToList();
    }

    private static Dictionary<string, string> EnsureVariables(GameRuntimeData runtime)
        => runtime.Variables ??= new Dictionary<string, string>();

    private static void RestoreTokenStateUnlocked(
        Dictionary<string, string> variables,
        bool hadPreviousState,
        string? previousState)
    {
        if (hadPreviousState)
            variables[TerriasIds.SpiritArtifactTruthDebitTokensKey] = previousState ?? "";
        else
            variables.Remove(TerriasIds.SpiritArtifactTruthDebitTokensKey);
    }
}
