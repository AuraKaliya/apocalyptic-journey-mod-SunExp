# Confirmed decisions and viewing cadence

## Product contract

The user-approved replay unit is a confirmed decision plus its consequences.
Omit unsubmitted drag, hold, cancel and tentative selection feedback. Preserve
accepted cards even when damage is immune or an effect is countered. Skills,
end-turn and confirmed in-action selections are decisions; draws, discards,
burns, passives and partner actions caused by them remain causal consequences.

Replace witnessed input waits with the viewing policy's uniform short pause.
Preserve execution order and observed timing within an executing chain. Native
overlap must not become a serial card/actor/impact template. Continuous BGM or a
persistent HUD is not evidence that the battle is still executing. A transient
effect is not evidence that the player is thinking either.

## Ownership and evidence

- `ReplayDecisionCaptureV17` owns open input waits. The recorder emits immutable
  paired boundary events through its existing journal, and declares the timing
  capability in the UI descriptor. No separate durable writer or mutable wait
  span is needed.
- Native input/queue and action/hand lifecycle evidence admit a wait. Confirmed
  native action entry, end-turn and accepted selection close it before effects.
  Read the matching Managed/decompile for acceptance semantics: a button click,
  `ActorTurnStarted`, `SourceCompleted`, or a global animation flag alone is not
  a decision boundary. The recording transaction may still be open while a
  choice awaits input.
  `OtherTurn` is only a witness that this recorded perspective awaits remote
  actions, not proof of what another human is doing. Keep all received state
  changes and observed execution spans (including late presentation); do not
  infer a remote player's private decision state or remove their effects.
- `ReplayDecisionTimelineV17` derives viewing coordinates without modifying the
  sealed document, event/state hashes or supported source format. State changes
  stay in causal sequence; transient offsets and audio sample starts/durations
  use the same mapping as event times and checkpoints.
- Native arrival/layout and committed discard/burn remain observed tracks.
  Suppressing speculative input must not lose draws, immediate consumption,
  redraws, or automatic hand reflow. Test interaction during hand animation.
  Drag, hover and tentative selection can each take over a native tween. Close
  that automatic observation before input moves it. Distinguish selection open
  from selection committed: native retention can reflow before resetting its UI.
- Protect observed execution spans, including late-arriving tracks, before
  shortening a witnessed wait. Zero-length waits inside protected execution
  must not generate inverted intervals. Drop audio cues fully removed by a cut;
  zero audio duration is an existing full-clip sentinel, not silence.
- Older sealed records without input witnesses retain their original cadence
  with an explicit UI explanation. Never guess missing boundaries or silently
  rewrite them into the new contract. This is data compatibility in one player,
  not an alternate recorder or heuristic playback mode.

## Verification

Use the replay behavior suite for different wait lengths with identical
consequences, nested selections, cancelled input, terminal/reset ownership,
malformed/overlapping waits, state hash preservation, checkpoint seek, overlap,
audio cue alignment and unchanged sealed bytes. Verify capability enforcement
on finalization/import and explicit original-cadence behavior for older records.

Use Unity/native adapter fixtures for accepted versus rejected confirmation,
input during card arrival, cancel-triggered layout, and native queue readiness.
Real-game acceptance must additionally cover held/returned cards, a zero-effect
accepted card, skill, end-turn, choice within a chain, partner/remote actions,
seek/speed and exported audio/video. Compare semantic anchors and viewing
duration, then close/reopen and enter the next battle. Report unavailable runtime
evidence separately; compilation and pure timing tests cannot prove host ordering.
