<script lang="ts">
  import ActiveGoalPane from './ActiveGoalPane.svelte';
  import SessionPlanPane from './SessionPlanPane.svelte';

  let {
    conversationId = '',
    planCollapsed = false,
    goalCollapsed = false,
    onTogglePlan = () => {},
    onToggleGoal = () => {},
    onOpenGoals = () => {},
  }: {
    conversationId?: string;
    planCollapsed?: boolean;
    goalCollapsed?: boolean;
    /** @deprecated kept so App.svelte props still type-check */
    loopCollapsed?: boolean;
    remindCollapsed?: boolean;
    onTogglePlan?: () => void;
    onToggleGoal?: () => void;
    onToggleLoop?: () => void;
    onToggleRemind?: () => void;
    onOpenGoals?: () => void;
    onOpenTasks?: () => void;
    onOpenProactive?: () => void;
  } = $props();
</script>

<div class="session-agent-docks" aria-label="会话目标与计划">
  <!-- 聊天顶栏只放会话强相关：Goal / Plan。提醒与循环在「任务」页，AI 关心在「主动」页。 -->
  <ActiveGoalPane collapsed={goalCollapsed} onToggle={onToggleGoal} {onOpenGoals} />
  {#if conversationId}
    <SessionPlanPane {conversationId} collapsed={planCollapsed} onToggle={onTogglePlan} />
  {/if}
</div>
