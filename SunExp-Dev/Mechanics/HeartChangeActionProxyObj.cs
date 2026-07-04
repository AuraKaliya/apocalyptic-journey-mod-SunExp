using System.Collections;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public sealed class HeartChangeActionProxyObj : OtherObj
{
    public override string Type => "Projection";

    public ObjectAction BuildProjectedAction(Enemy source, ObjectCard sourceCard)
    {
        Status = source.Status;
        InstanceId = source.InstanceId;
        dataConfig = source.dataConfig;
        data = source.data;

        var projectedCard = new ObjectCard
        {
            status = source.Status as StatusManager,
            isIgnored = false,
            nowCD = 0,
            keyWords = sourceCard.keyWords == null
                ? new List<string>()
                : new List<string>(sourceCard.keyWords)
        };
        projectedCard.Init(sourceCard.dataConfig);

        var action = new ObjectAction(this);
        action.AddCard(projectedCard);
        ActionCards = action.ActionShow(1);
        FightAction = action;
        return action;
    }

    public override IEnumerator DoAction()
    {
        yield break;
    }
}
