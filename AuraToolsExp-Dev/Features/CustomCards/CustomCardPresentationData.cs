using System;
using System.Collections.Generic;
using System.Globalization;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>One field projection for both the native draft view and the crafted card. Never contains executable scripts.</summary>
internal static class CustomCardPresentationData
{
    internal static Dictionary<string,string> Create(CustomCardDocument document,CustomCardCompilation compilation,string id)
    {
        return new(StringComparer.Ordinal)
        {
            ["Id"]=id,["Name"]=document.Name,["Expend"]=document.Cost.ToString(CultureInfo.InvariantCulture),
            ["Rarity"]=document.Rarity.ToString(CultureInfo.InvariantCulture),["Type"]=document.Targeted?"攻击牌":"技能牌",
            ["Tag"]=(document.Burnout?"Burnout,":"")+(document.Retain?"Retain,":""),
            ["Description"]=compilation.Success?compilation.Description:"效果尚未完成",
            ["Note"]=document.Note,["Effects"]="",["Icon"]=document.Artwork.TemplateIcon
        };
    }
}
