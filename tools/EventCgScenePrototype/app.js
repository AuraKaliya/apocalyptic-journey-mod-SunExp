(() => {
  "use strict";

  const sceneProfiles = {
    opening: {
      title: "战斗开场",
      subtitle: "队伍介绍",
      profile: "名单揭示 · 开场姿态",
      layoutPrefix: "快速入场"
    },
    victory: {
      title: "普通胜利",
      subtitle: "冒险队伍",
      profile: "自适应群像 · 普通胜利",
      layoutPrefix: "等权群像"
    },
    midas: {
      title: "点金手胜利",
      subtitle: "财富终局",
      profile: "自适应群像 · 金色主题覆盖",
      layoutPrefix: "金色庆祝"
    },
    ritual: {
      title: "仪式胜利",
      subtitle: "仪式终局",
      profile: "自适应群像 · 仪式主题覆盖",
      layoutPrefix: "阵式庆祝"
    },
    curse: {
      title: "诅咒胜利",
      subtitle: "七咒终局",
      profile: "自适应群像 · 诅咒主题覆盖",
      layoutPrefix: "暗色庆祝"
    },
    defeat: {
      title: "战斗失败",
      subtitle: "队伍撤退",
      profile: "安静群像 · 失败姿态",
      layoutPrefix: "低位收敛"
    },
    settlement: {
      title: "冒险结算",
      subtitle: "旅途留影",
      profile: "旅途合照 · 结算姿态",
      layoutPrefix: "档案合照"
    }
  };

  const roster = [
    {
      name: "乌娜",
      src: "../../AuraToolsExp/SharedResources/Skins/career_1/summer_cool/Idle/matte_00001.png",
      visibleW: .63,
      visibleH: .84,
      leftPad: .14,
      bottomPad: .08,
      kind: "humanoid"
    },
    {
      name: "洛奈尔",
      src: "../../Terrias/ModResource/AnimationLib/Loneer/Idle/Idle-01.png",
      visibleW: 1,
      visibleH: .99,
      leftPad: 0,
      bottomPad: .01,
      kind: "wide"
    },
    {
      name: "哥伦比娅",
      src: "../../AuraToolsExp/SharedResources/Skins/Terrias_columbina_columbina/DoByHand/Idle/frame_01.png",
      visibleW: .54,
      visibleH: .93,
      leftPad: .23,
      bottomPad: .06,
      kind: "humanoid"
    },
    {
      name: "乌娜·异相",
      src: "../../Terrias/ModResource/AnimationLib/WuNa_e/Idle/Idle_00.png",
      visibleW: .68,
      visibleH: .95,
      leftPad: .17,
      bottomPad: .04,
      kind: "humanoid"
    },
    {
      name: "暮影",
      src: "../../Terrias/ModResource/AnimationLib/Dusk/Idle/Idle_00.png",
      visibleW: .80,
      visibleH: .93,
      leftPad: .12,
      bottomPad: .04,
      kind: "humanoid"
    },
    {
      name: "哥伦比娅·原型",
      src: "../../Terrias/ModResource/AnimationLib/columbina/Idle/frame_01.png",
      visibleW: .70,
      visibleH: .91,
      leftPad: .16,
      bottomPad: .10,
      kind: "humanoid"
    },
    {
      name: "第二日轮",
      src: "../../Terrias/ModResource/AnimationLib/SecondSunWeel_e/Idle/Idle_00.png",
      visibleW: .93,
      visibleH: .98,
      leftPad: .05,
      bottomPad: 0,
      kind: "object"
    },
    {
      name: "乌娜·晨星",
      src: "../../Terrias/ModResource/AnimationLib/WuNa/Idle/frame_0001.png",
      visibleW: .77,
      visibleH: .89,
      leftPad: .11,
      bottomPad: .06,
      kind: "humanoid"
    }
  ];

  const state = {
    scene: "victory",
    count: 4,
    motion: "full",
    safeZone: false
  };

  const elements = {
    scene: document.querySelector("#scene"),
    participants: document.querySelector("#participants"),
    sceneTitle: document.querySelector("#scene-title"),
    sceneSubtitle: document.querySelector("#scene-subtitle"),
    profileLabel: document.querySelector("#profile-label"),
    layoutStatus: document.querySelector("#layout-status"),
    assetStatus: document.querySelector("#asset-status"),
    compositionNote: document.querySelector("#composition-note"),
    count: document.querySelector("#participant-count"),
    countDown: document.querySelector("#count-down"),
    countUp: document.querySelector("#count-up"),
    safeZone: document.querySelector("#safe-zone-toggle")
  };

  function layoutFor(count, scene) {
    if (count >= 7) {
      return { mode: "panels", name: count === 7 ? "宽体回退 · 4+3 肖像面板" : "宽体回退 · 4+4 肖像面板", slots: [] };
    }

    const layouts = {
      1: [{ x: 50, bottom: 2, width: 38, height: 78, z: 3 }],
      2: [
        { x: 34, bottom: 1, width: 37, height: 69, z: 3 },
        { x: 66, bottom: 1, width: 37, height: 69, z: 3 }
      ],
      3: [
        { x: 24, bottom: 1, width: 32, height: 62, z: 2 },
        { x: 50, bottom: 1, width: 34, height: 70, z: 4 },
        { x: 76, bottom: 1, width: 32, height: 62, z: 2 }
      ],
      4: [
        { x: 16, bottom: 0, width: 28, height: 61, z: 2 },
        { x: 39, bottom: 1, width: 29, height: 66, z: 4 },
        { x: 61, bottom: 1, width: 29, height: 66, z: 4 },
        { x: 84, bottom: 0, width: 28, height: 61, z: 2 }
      ],
      5: [
        { x: 35, bottom: 52, width: 24, height: 41, z: 1 },
        { x: 65, bottom: 52, width: 24, height: 41, z: 1 },
        { x: 20, bottom: 0, width: 25, height: 44, z: 4 },
        { x: 50, bottom: 0, width: 26, height: 46, z: 5 },
        { x: 80, bottom: 0, width: 25, height: 44, z: 4 }
      ],
      6: [
        { x: 31, bottom: 51, width: 23, height: 40, z: 1 },
        { x: 52, bottom: 54, width: 23, height: 40, z: 1 },
        { x: 73, bottom: 51, width: 23, height: 40, z: 1 },
        { x: 18, bottom: 0, width: 24, height: 42, z: 4 },
        { x: 50, bottom: 0, width: 25, height: 44, z: 5 },
        { x: 82, bottom: 0, width: 24, height: 42, z: 4 }
      ]
    };
    const names = {
      1: "单人主视觉",
      2: "向内双人",
      3: "中心三角",
      4: "等权弧形",
      5: "3+2 错层",
      6: "3+3 错层"
    };
    const slots = layouts[count].map(slot => ({ ...slot }));
    if (scene === "defeat") {
      slots.forEach(slot => {
        slot.bottom = Math.max(0, slot.bottom - 3);
        slot.height *= .94;
      });
    }
    return { mode: "tableau", name: names[count], slots };
  }

  function createParticipant(person, index, slot, panelMode) {
    const figure = document.createElement("figure");
    figure.className = "participant";
    figure.dataset.index = String(index);
    figure.dataset.kind = person.kind;
    figure.style.setProperty("--delay", `${Math.min(.42, index * .07)}s`);

    const visibleCenter = person.leftPad + person.visibleW / 2;
    const normalizedHeight = 100 / person.visibleH;
    const bottomShift = -person.bottomPad / person.visibleH * 100;
    const centerShift = (0.5 - visibleCenter) * 100;
    figure.style.setProperty("--normalized-height", `${normalizedHeight.toFixed(2)}%`);
    figure.style.setProperty("--bottom-shift", `${bottomShift.toFixed(2)}%`);
    figure.style.setProperty("--center-shift", `${centerShift.toFixed(2)}%`);
    figure.style.setProperty("--panel-height", `${Math.min(178, 114 / person.visibleH).toFixed(2)}%`);
    figure.style.setProperty("--panel-bottom-shift", `${(-person.bottomPad / person.visibleH * 112).toFixed(2)}%`);

    if (!panelMode) {
      figure.style.setProperty("--left", `${slot.x}%`);
      figure.style.setProperty("--bottom", `${slot.bottom}%`);
      figure.style.setProperty("--width", `${slot.width}%`);
      figure.style.setProperty("--height", `${slot.height}%`);
      figure.style.setProperty("--z", String(slot.z));
      if (slot.x > 50) figure.classList.add("is-mirrored");
    }

    const art = document.createElement("div");
    art.className = "art-frame";
    const image = document.createElement("img");
    image.src = person.src;
    image.alt = person.name;
    art.appendChild(image);
    const caption = document.createElement("figcaption");
    caption.textContent = person.name;
    figure.append(art, caption);
    return figure;
  }

  function configurePanelGrid(figures, count) {
    elements.participants.style.gridTemplateColumns = "repeat(8, minmax(0, 1fr))";
    elements.participants.style.gridTemplateRows = "repeat(2, minmax(0, 1fr))";
    figures.forEach((figure, index) => {
      const inTopRow = index < 4;
      const rowIndex = inTopRow ? index : index - 4;
      const lowerCount = count - 4;
      const columns = inTopRow
        ? [1, 3, 5, 7]
        : lowerCount === 3 ? [2, 4, 6] : [1, 3, 5, 7];
      figure.style.gridColumn = `${columns[rowIndex]} / span 2`;
      figure.style.gridRow = inTopRow ? "1" : "2";
    });
  }

  function render() {
    const profile = sceneProfiles[state.scene];
    const layout = layoutFor(state.count, state.scene);
    const selectedRoster = roster.slice(0, state.count);
    const panelMode = layout.mode === "panels";

    elements.scene.className = `scene scene-${state.scene}`;
    elements.scene.classList.toggle("reduced-motion", state.motion === "reduced");
    elements.scene.classList.toggle("show-safe-zone", state.safeZone);
    elements.scene.dataset.count = String(state.count);
    elements.scene.dataset.layout = layout.mode;
    elements.sceneTitle.textContent = profile.title;
    elements.sceneSubtitle.textContent = profile.subtitle;
    elements.profileLabel.textContent = profile.profile;
    elements.layoutStatus.textContent = `${profile.layoutPrefix} · ${layout.name}`;
    elements.assetStatus.textContent = `资源归一化 ${state.count}/${state.count}`;
    elements.compositionNote.textContent = panelMode
      ? `${state.count} 人包含宽体/非人形资源，自动切换等权肖像面板`
      : `${state.count} 人${layout.name}，按联合 Alpha 边界归一化`;
    elements.count.textContent = String(state.count);
    elements.countDown.disabled = state.count <= 1;
    elements.countUp.disabled = state.count >= 8;
    elements.safeZone.checked = state.safeZone;

    document.querySelectorAll("[data-scene]").forEach(button => {
      button.classList.toggle("is-active", button.dataset.scene === state.scene);
    });
    document.querySelectorAll("[data-motion]").forEach(button => {
      button.classList.toggle("is-active", button.dataset.motion === state.motion);
    });

    elements.participants.replaceChildren();
    elements.participants.removeAttribute("style");
    const figures = selectedRoster.map((person, index) => createParticipant(
      person,
      index,
      panelMode ? null : layout.slots[index],
      panelMode));
    if (panelMode) configurePanelGrid(figures, state.count);
    elements.participants.append(...figures);

    waitForImages().then(() => {
      document.body.dataset.previewReady = "true";
    });
  }

  function waitForImages() {
    const images = [...document.images];
    return Promise.all(images.map(image => {
      if (image.complete && image.naturalWidth > 0) return Promise.resolve();
      return new Promise(resolve => {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", resolve, { once: true });
      });
    }));
  }

  function setState(next) {
    if (next.scene && sceneProfiles[next.scene]) state.scene = next.scene;
    if (Number.isFinite(next.count)) state.count = Math.max(1, Math.min(8, Math.round(next.count)));
    if (next.motion === "full" || next.motion === "reduced") state.motion = next.motion;
    if (typeof next.safeZone === "boolean") state.safeZone = next.safeZone;
    document.body.dataset.previewReady = "false";
    render();
  }

  document.querySelectorAll("[data-scene]").forEach(button => {
    button.addEventListener("click", () => setState({ scene: button.dataset.scene }));
  });
  document.querySelectorAll("[data-motion]").forEach(button => {
    button.addEventListener("click", () => setState({ motion: button.dataset.motion }));
  });
  elements.countDown.addEventListener("click", () => setState({ count: state.count - 1 }));
  elements.countUp.addEventListener("click", () => setState({ count: state.count + 1 }));
  elements.safeZone.addEventListener("change", event => setState({ safeZone: event.target.checked }));

  window.EventCgPrototype = {
    setState,
    getState: () => ({ ...state }),
    diagnostics: () => ({
      participantCount: elements.participants.children.length,
      layout: elements.scene.dataset.layout,
      scene: state.scene,
      motion: state.motion,
      imagesReady: [...document.images].every(image => image.complete && image.naturalWidth > 0)
    })
  };

  render();
})();
