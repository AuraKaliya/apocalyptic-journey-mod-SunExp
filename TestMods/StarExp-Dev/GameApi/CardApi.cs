using System;
using StarExp.Dll.Infrastructure;

namespace StarExp.Dll.GameApi;

public static class CardApi
{
    public static void AddCardToHand(ScriptExecutor self, string cardId)
    {
        try
        {
            self.AddCardById(cardId);
        }
        catch
        {
            try
            {
                self.AddCardByData(cardId, "");
            }
            catch (Exception ex)
            {
                StarExpLog.Warn("AddCardToHand fallback used: cardId=" + cardId + ", error=" + ex.Message);
                self.AddCard(cardId);
            }
        }
    }
}
