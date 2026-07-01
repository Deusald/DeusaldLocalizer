using DeusaldLocalizerCommon;
using Newtonsoft.Json;

namespace App;

public class BaseBackendService(ProjectStateService projectState)
{
    public virtual bool IsOnline => false;

    #region Users

    public async Task SetAcceptNewMembersAsync(bool acceptNewMembers)
    {
        bool oldValue = projectState.CurrentProject!.AcceptNewMembers;
        projectState.CurrentProject!.AcceptNewMembers = acceptNewMembers;
        projectState.MarkDirty();
        bool result = await OnlineSetAcceptNewMembersAsync(acceptNewMembers);
        if (result)
        {
            AddToProjectHistory("Members-AcceptNew", projectState.CurrentProject.Id, HistoryChangeType.Updated, oldValue.ToString(), acceptNewMembers.ToString());
            projectState.MarkDirty();
            return;
        }
        projectState.CurrentProject!.AcceptNewMembers = oldValue;
        projectState.MarkDirty();
    }

    protected virtual Task<bool> OnlineSetAcceptNewMembersAsync(bool acceptNewMembers)
    {
        return Task.FromResult(true);
    }

    public async Task SetNewUserPermissionsAsync(ProjectMemberDto member, PermissionFlags newPermissions)
    {
        PermissionFlags oldPermissions = member.Permissions;
        member.Permissions = newPermissions;
        projectState.MarkDirty();
        bool result = await OnlineSetNewUserPermissionsAsync(member, newPermissions);
        if (result)
        {
            AddToProjectHistory("Members-Permissions", member.UserId, HistoryChangeType.Updated, JsonConvert.SerializeObject(oldPermissions), JsonConvert.SerializeObject(newPermissions));
            projectState.MarkDirty();
            return;
        }
        member.Permissions = oldPermissions;
        projectState.MarkDirty();
    }

    protected virtual Task<bool> OnlineSetNewUserPermissionsAsync(ProjectMemberDto member, PermissionFlags newPermissions)
    {
        return Task.FromResult(true);
    }

    public async Task RemoveUserAsync(ProjectMemberDto member)
    {
        bool   result     = await OnlineRemoveUserAsync(member);
        if (!result) return;
        projectState.CurrentProject!.Members.Remove(member);
        AddToProjectHistory("Members-Removed", member.UserId, HistoryChangeType.Deleted, JsonConvert.SerializeObject(member), string.Empty);
        projectState.MarkDirty();
    }

    protected virtual Task<bool> OnlineRemoveUserAsync(ProjectMemberDto member)
    {
        return Task.FromResult(true);
    }

    public virtual Task<(bool, string)> OnlineChangePasswordAsync(string newPassword)
    {
        return Task.FromResult((true, string.Empty));
    }

    #endregion Users

    #region Languages

    public async Task AddLanguageAsync(string code)
    {
        bool result = await OnlineAddLanguageAsync(code);
        if (!result) return;
        projectState.CurrentProject!.Languages.Add(code);
        AddToProjectHistory("Languages", projectState.CurrentProject.Id, HistoryChangeType.Created, string.Empty, code);
        projectState.MarkDirty();
    }

    protected virtual Task<bool> OnlineAddLanguageAsync(string code)
    {
        return Task.FromResult(true);
    }
    
    public async Task RemoveLanguageAsync(string code)
    {
        bool result = await OnlineRemoveLanguageAsync(code);
        if (!result) return;
        projectState.CurrentProject!.Languages.Remove(code);
        AddToProjectHistory("Languages", projectState.CurrentProject.Id, HistoryChangeType.Deleted, code, string.Empty);
        projectState.MarkDirty();
    }

    protected virtual Task<bool> OnlineRemoveLanguageAsync(string code)
    {
        return Task.FromResult(true);
    }

    #endregion Languages
    
    private void AddToProjectHistory(string entityPartName, Guid entityPartId, HistoryChangeType changeType, string oldValue, string newValue)
    {
        projectState.CurrentProject!.ProjectHistory.Add(new HistoryEntryDto
        {
            EntityPartName = entityPartName,
            EntityPartId   = entityPartId,
            ChangeType     = changeType,
            OldValue       = oldValue,
            NewValue       = newValue,
            UserId         = projectState.CurrentUser.Id,
            UserName       = projectState.CurrentUser.DisplayName
        });
    }
}