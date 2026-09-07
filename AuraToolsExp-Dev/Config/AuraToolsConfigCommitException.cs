using System.IO;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsConfigCommitException : IOException
{
    public AuraToolsConfigCommitException(string moduleId)
        : base("设置保存失败，已恢复上一次提交的值。请检查文件权限或重新加载配置后再试。") => ModuleId = moduleId;
    public string ModuleId { get; }
}
