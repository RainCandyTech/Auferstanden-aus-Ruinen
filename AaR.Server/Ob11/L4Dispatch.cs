namespace AaR.Server.Ob11;

/// <summary>
/// L4v1 能力分派表：OB11 action → 所需扩展组（docs/l4-contract-v1.md §6）。
/// null = core-v1 或服务端本地动作（不触发守卫）。只收**已验证映射**——未列动作不守卫。
/// 发送动作按 core 放行；媒体元素级的 ext-media 分派由内核错误面自然承担（契约 §3.4）。
/// </summary>
public static class L4Dispatch
{
    public const string Media = "ext-media";
    public const string MsgInfo = "ext-msginfo";
    public const string History = "ext-history";
    public const string Forward = "ext-forward";
    public const string GroupRead = "ext-group-read";
    public const string GroupAdmin = "ext-group-admin";
    public const string Buddy = "ext-buddy";
    public const string User = "ext-user";
    public const string Recent = "ext-recent";
    public const string Debug = "ext-debug";

    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // ext-forward
        ["send_group_forward_msg"] = Forward,
        ["send_private_forward_msg"] = Forward,
        ["get_forward_msg"] = Forward,
        // ext-msginfo
        ["delete_msg"] = MsgInfo,
        ["get_msg"] = MsgInfo,
        ["mark_msg_as_read"] = MsgInfo,
        ["set_msg_emoji_like"] = MsgInfo,
        ["send_poke"] = MsgInfo,
        ["group_poke"] = MsgInfo,
        ["friend_poke"] = MsgInfo,
        ["send_input_status"] = MsgInfo,
        // ext-history
        ["get_group_msg_history"] = History,
        ["get_friend_msg_history"] = History,
        // ext-buddy
        ["get_friend_list"] = Buddy,
        ["set_friend_add_request"] = Buddy,
        // ext-group-read
        ["get_group_list"] = GroupRead,
        ["get_group_info"] = GroupRead,
        ["get_group_member_list"] = GroupRead,
        ["get_group_member_info"] = GroupRead,
        ["get_group_shut_list"] = GroupRead,
        ["get_group_at_all_remain"] = GroupRead,
        ["get_essence_msg_list"] = GroupRead,
        // ext-group-admin
        ["set_group_kick"] = GroupAdmin,
        ["set_group_ban"] = GroupAdmin,
        ["set_group_anonymous_ban"] = GroupAdmin,
        ["set_group_whole_ban"] = GroupAdmin,
        ["set_group_admin"] = GroupAdmin,
        ["set_group_card"] = GroupAdmin,
        ["set_group_special_title"] = GroupAdmin,
        ["set_group_name"] = GroupAdmin,
        ["set_group_leave"] = GroupAdmin,
        ["set_essence_msg"] = GroupAdmin,
        ["del_essence_msg"] = GroupAdmin,
        ["send_group_notice"] = GroupAdmin,
        // ext-media
        ["get_image"] = Media,
        ["get_record"] = Media,
        ["get_file"] = Media,
        ["download_file"] = Media,
        // ext-user / ext-recent / ext-debug
        ["get_stranger_info"] = User,
        ["get_recent_contact"] = Recent,
        ["debug_kernel"] = Debug,
    };

    /// <summary>所需扩展组名；null = core-v1 / 服务端本地，放行。</summary>
    public static string? RequiredExt(string action)
        => Map.TryGetValue(action, out var ext) ? ext : null;
}
