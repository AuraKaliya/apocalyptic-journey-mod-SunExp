using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Data.Save;

namespace SunExp.Dll.Mechanics;

public static class SunExpHardTagState
{
    public static bool Active(string id)
    {
        return Level(id) > 0;
    }

    public static int Level(string id)
    {
        var level = 0;
        foreach (var tag in CurrentHardTags())
        {
            var tagId = DictionaryUtil.Get(tag?.data, "Id");
            if (SunExpHardTagIds.Same(tagId, id))
            {
                level++;
            }
        }

        return level;
    }

    private static IEnumerable<DataConfig?> CurrentHardTags()
    {
        List<DataConfig>? tags = null;
        try
        {
            tags = GameSaveManager.GetHardTags();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Hard tag read skipped: " + ex.Message);
        }

        if (tags == null)
        {
            yield break;
        }

        foreach (var tag in tags)
        {
            yield return tag;
        }
    }
}
