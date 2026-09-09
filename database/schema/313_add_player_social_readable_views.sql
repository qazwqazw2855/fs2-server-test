DROP VIEW IF EXISTS `god2_player`.`vw_player_relationships_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_player_social_blocks_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_player_social_invitations_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_player_social_operations_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_blackbox_social_operation_observations_readable`;

CREATE VIEW `god2_player`.`vw_player_relationships_readable` AS
SELECT
    relationship.`character_id_low` AS `角色A_ID`,
    COALESCE(character_low.`name`, '') AS `角色A名稱`,
    relationship.`character_id_high` AS `角色B_ID`,
    COALESCE(character_high.`name`, '') AS `角色B名稱`,
    relationship.`relationship_kind` AS `關係類型`,
    relationship.`status` AS `關係狀態`,
    relationship.`version` AS `版本`,
    CASE
        WHEN relationship.`relationship_kind` = 'Friend' AND relationship.`status` = 'Active' THEN '好友關係成立'
        WHEN relationship.`status` = 'Ended' THEN '關係已解除'
        ELSE '關係待確認'
    END AS `功能對照`,
    relationship.`created_at_utc` AS `建立時間UTC`,
    relationship.`ended_at_utc` AS `結束時間UTC`
FROM `god2_player`.`player_relationships` relationship
LEFT JOIN `god2_player`.`characters` character_low
    ON character_low.`character_id` = relationship.`character_id_low`
LEFT JOIN `god2_player`.`characters` character_high
    ON character_high.`character_id` = relationship.`character_id_high`;

CREATE VIEW `god2_player`.`vw_player_social_blocks_readable` AS
SELECT
    block_row.`blocker_character_id` AS `封鎖者角色ID`,
    COALESCE(blocker.`name`, '') AS `封鎖者名稱`,
    block_row.`blocked_character_id` AS `被封鎖角色ID`,
    COALESCE(blocked.`name`, '') AS `被封鎖者名稱`,
    '封鎖名單' AS `功能對照`,
    block_row.`created_at_utc` AS `建立時間UTC`
FROM `god2_player`.`player_social_blocks` block_row
LEFT JOIN `god2_player`.`characters` blocker
    ON blocker.`character_id` = block_row.`blocker_character_id`
LEFT JOIN `god2_player`.`characters` blocked
    ON blocked.`character_id` = block_row.`blocked_character_id`;

CREATE VIEW `god2_player`.`vw_player_social_invitations_readable` AS
SELECT
    invitation.`invitation_id` AS `邀請ID`,
    invitation.`kind` AS `邀請類型`,
    invitation.`requester_character_id` AS `邀請者角色ID`,
    COALESCE(requester.`name`, '') AS `邀請者名稱`,
    invitation.`target_character_id` AS `目標角色ID`,
    COALESCE(target.`name`, '') AS `目標角色名稱`,
    invitation.`status` AS `邀請狀態`,
    invitation.`response_actor_id` AS `回應者角色ID`,
    COALESCE(responder.`name`, '') AS `回應者名稱`,
    invitation.`version` AS `版本`,
    CASE
        WHEN invitation.`kind` = 'Friend' AND invitation.`status` = 'Pending' THEN '好友邀請等待回應'
        WHEN invitation.`kind` = 'Friend' AND invitation.`status` = 'Accepted' THEN '好友邀請已接受'
        WHEN invitation.`kind` = 'Friend' AND invitation.`status` = 'Rejected' THEN '好友邀請已拒絕'
        WHEN invitation.`status` = 'Cancelled' THEN '邀請已取消'
        ELSE '邀請狀態待確認'
    END AS `功能對照`,
    invitation.`requested_at_utc` AS `送出時間UTC`,
    invitation.`responded_at_utc` AS `回應時間UTC`
FROM `god2_player`.`player_social_invitations` invitation
LEFT JOIN `god2_player`.`characters` requester
    ON requester.`character_id` = invitation.`requester_character_id`
LEFT JOIN `god2_player`.`characters` target
    ON target.`character_id` = invitation.`target_character_id`
LEFT JOIN `god2_player`.`characters` responder
    ON responder.`character_id` = invitation.`response_actor_id`;

CREATE VIEW `god2_player`.`vw_player_social_operations_readable` AS
SELECT
    operation.`actor_character_id` AS `操作者角色ID`,
    COALESCE(actor.`name`, '') AS `操作者名稱`,
    operation.`request_id` AS `請求ID`,
    operation.`operation_kind` AS `操作類型`,
    operation.`result_code` AS `結果代碼`,
    operation.`invitation_id` AS `邀請ID`,
    operation.`related_character_id` AS `相關角色ID`,
    COALESCE(related.`name`, '') AS `相關角色名稱`,
    CASE WHEN operation.`mutated` = 1 THEN '有異動' ELSE '無異動' END AS `是否異動`,
    CASE WHEN operation.`blocked` = 1 THEN '被封鎖阻擋' ELSE '未被封鎖阻擋' END AS `封鎖判定`,
    CASE
        WHEN operation.`operation_kind` LIKE '%Invite%' THEN '送出社交邀請'
        WHEN operation.`operation_kind` LIKE '%Accept%' THEN '接受社交邀請'
        WHEN operation.`operation_kind` LIKE '%Reject%' THEN '拒絕社交邀請'
        WHEN operation.`operation_kind` LIKE '%Cancel%' THEN '取消社交邀請'
        WHEN operation.`operation_kind` LIKE '%Block%' THEN '封鎖或解除封鎖'
        WHEN operation.`operation_kind` LIKE '%Remove%' THEN '解除好友或移除關係'
        ELSE '社交操作'
    END AS `功能對照`,
    operation.`created_at_utc` AS `建立時間UTC`
FROM `god2_player`.`player_social_operations` operation
LEFT JOIN `god2_player`.`characters` actor
    ON actor.`character_id` = operation.`actor_character_id`
LEFT JOIN `god2_player`.`characters` related
    ON related.`character_id` = operation.`related_character_id`;

CREATE VIEW `god2_player`.`vw_blackbox_social_operation_observations_readable` AS
SELECT
    operation_view.`操作者角色ID`,
    operation_view.`操作者名稱`,
    operation_view.`操作類型`,
    operation_view.`結果代碼`,
    operation_view.`相關角色ID`,
    operation_view.`相關角色名稱`,
    operation_view.`是否異動`,
    operation_view.`封鎖判定`,
    operation_view.`功能對照`,
    CASE
        WHEN operation_view.`結果代碼` = 'Success' THEN '第一優先：確認客戶端社交列表/邀請視窗同步'
        WHEN operation_view.`封鎖判定` = '被封鎖阻擋' THEN '第一優先：確認封鎖後邀請與私訊是否被阻擋'
        ELSE '第二優先：確認失敗提示與重放保護'
    END AS `黑箱測試優先級`,
    '測好友邀請、接受、拒絕、取消、解除好友、封鎖、解除封鎖與重放保護。' AS `測試重點`,
    operation_view.`建立時間UTC`
FROM `god2_player`.`vw_player_social_operations_readable` operation_view;
