// Mock API server for frontend development/testing.
// Run with: yarn mock (or: node mock-server.mjs)
// Then run: yarn start — the Parcel proxy forwards /api/* to this server.
import { createHash, randomBytes, randomUUID } from "node:crypto";
import { createServer } from "node:http";

const PORT = parseInt(process.env.MOCK_PORT ?? "5097", 10);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function json(res, data, status = 200) {
  res.writeHead(status, {
    "Content-Type": "application/json",
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
    "Access-Control-Allow-Methods": "GET, POST, PUT, PATCH, DELETE, OPTIONS",
  });
  res.end(JSON.stringify(data));
}

function empty(res, status = 200) {
  res.writeHead(status, {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
    "Access-Control-Allow-Methods": "GET, POST, PUT, PATCH, DELETE, OPTIONS",
  });
  res.end();
}

function mockPoster(res, fileName) {
  const hue = [...fileName].reduce(
    (value, character) => (value * 31 + character.codePointAt(0)) % 360,
    24,
  );
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 450" role="img" aria-label="Mock poster">
      <defs>
        <linearGradient id="paper" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="hsl(${hue} 42% 82%)" />
          <stop offset="1" stop-color="hsl(${(hue + 38) % 360} 34% 58%)" />
        </linearGradient>
      </defs>
      <rect width="300" height="450" fill="url(#paper)" />
      <circle cx="150" cy="175" r="72" fill="rgba(255,255,255,.28)" />
      <path d="M70 385c18-82 52-122 80-122s62 40 80 122" fill="rgba(255,255,255,.3)" />
      <text x="150" y="420" text-anchor="middle" fill="rgba(35,28,24,.72)" font-family="serif" font-size="24">SDW MOCK</text>
    </svg>`;
  res.writeHead(200, {
    "Content-Type": "image/svg+xml; charset=utf-8",
    "Cache-Control": "private, max-age=3600",
    "X-Content-Type-Options": "nosniff",
    "Access-Control-Allow-Origin": "*",
  });
  res.end(svg);
}

function readBody(req) {
  return new Promise((resolve) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString();
      try {
        resolve(JSON.parse(raw));
      } catch {
        resolve({});
      }
    });
  });
}

function fakeToken() {
  return randomBytes(64).toString("base64url");
}

function hasAuth(req) {
  return !!req.headers.authorization?.startsWith("Bearer ");
}

// ---------------------------------------------------------------------------
// Mock data
// ---------------------------------------------------------------------------

let registered = false;
const activeRefreshTokens = new Set();

function issueAuth() {
  const refreshToken = fakeToken();
  activeRefreshTokens.add(refreshToken);
  return {
    token: fakeToken(),
    refreshToken,
    success: true,
  };
}

const ANIME_TITLES = [
  {
    title: "[Mikanani] 葬送的芙莉莲 / Sousou no Frieren - 28 (1080p)",
    desc: "勇者一行的魔法使芙莉莲的旅途故事",
    season: 1,
    episode: 28,
    animeName: "葬送的芙莉莲",
    originalName: "Sousou no Frieren",
    tmdbId: "209867",
    posterPath: "/dqZENchTd7lp5zht7BdlqM7RBhD.jpg",
  },
  {
    title: "[SubsPlease] 迷宫饭 / Dungeon Meshi - 24 (1080p)",
    desc: "在地下城中烹饪魔物的冒险者们",
    season: 1,
    episode: 24,
    animeName: "迷宫饭",
    originalName: "Dungeon Meshi",
    tmdbId: "220150",
    posterPath: "/b8dFp1MKnfJCMQvMfnYnBjPuqEu.jpg",
  },
  {
    title: "[Mikanani] 药屋少女的呢喃 / Kusuriya no Hitorigoto - 24 (1080p)",
    desc: "后宫药师猫猫的推理日常",
    season: 1,
    episode: 24,
    animeName: "药屋少女的呢喃",
    originalName: "Kusuriya no Hitorigoto",
    tmdbId: "229598",
    posterPath: "/hBsMO2fGMRYCFIApI2nkYCApzAb.jpg",
  },
  {
    title: "[ANi] 我心里危险的东西 第二季 - 13 (1080p)",
    desc: "市川与山田的青春恋爱物语",
    season: 2,
    episode: 13,
    animeName: "我心里危险的东西",
    originalName: "Boku no Kokoro no Yabai Yatsu",
    tmdbId: "203737",
    posterPath: "/qCHIPLBSfUMWS01qnJGnPXlSKGZ.jpg",
  },
  {
    title: "[Mikanani] 物语系列 Off & Monster Season - 12 (1080p)",
    desc: "阿良良木历的怪异故事继续",
    season: 1,
    episode: 12,
    animeName: "物语系列",
    originalName: "Monogatari Series",
    tmdbId: "46195",
    posterPath: "/oO0eeCAXsQQcq0DjOEJXNKlrBR2.jpg",
  },
  {
    title: "[SubsPlease] 鬼灭之刃 柱训练篇 - 08 (1080p)",
    desc: "炭治郎与柱们的训练篇章",
    season: 4,
    episode: 8,
    animeName: "鬼灭之刃",
    originalName: "Kimetsu no Yaiba",
    tmdbId: "85937",
    posterPath: "/wrC2TWAOPQMD4bpGjdwH7MjjPT3.jpg",
  },
  {
    title: "[Mikanani] 无职转生 第三季 - 12 (1080p)",
    desc: "鲁迪乌斯的异世界冒险续篇",
    season: 3,
    episode: 12,
    animeName: "无职转生",
    originalName: "Mushoku Tensei",
    tmdbId: "97986",
    posterPath: "/dBxxtfhC4vYISbxCDMRSpDDiO6B.jpg",
  },
  {
    title: "[ANi] 败犬女主太多了 - 12 (1080p)",
    desc: "温水和是被选中的那个男人",
    season: 1,
    episode: 12,
    animeName: "败犬女主太多了",
    originalName: "Make Heroine ga Oosugiru!",
    tmdbId: "253485",
    posterPath: "/wZVcuBejljRQJcR3lG6OofJDbeQ.jpg",
  },
  {
    title: "[SubsPlease] 夏日重现 / Summer Time Rendering - 25 (1080p)",
    desc: "小岛上的时间循环悬疑故事",
    season: 1,
    episode: 25,
    animeName: "夏日重现",
    originalName: "Summer Time Rendering",
    tmdbId: "125392",
    posterPath: "/aURJQ3AyBi1MCPaV1oGNysv5piI.jpg",
  },
  {
    title: "[Mikanani] 孤独摇滚 / Bocchi the Rock! - 12 (1080p)",
    desc: "社恐少女后藤一里的乐队之路",
    season: 1,
    episode: 12,
    animeName: "孤独摇滚",
    originalName: "Bocchi the Rock!",
    tmdbId: "203354",
    posterPath: "/yPCxMlsEJFsOlDiXUkLlAqL1gp0.jpg",
  },
  {
    title: "[ANi] 间谍过家家 第三季 - 12 (1080p)",
    desc: "黄昏一家的间谍喜剧日常",
    season: 3,
    episode: 12,
    animeName: "间谍过家家",
    originalName: "SPY×FAMILY",
    tmdbId: "110248",
    posterPath: "/3bWEMlYABPXCNYBZhcjyxLKoRBL.jpg",
  },
  {
    title: "[SubsPlease] 青之箱 / Blue Box - 24 (1080p)",
    desc: "大喜与千夏的恋爱与羽毛球",
    season: 1,
    episode: 24,
    animeName: "青之箱",
    originalName: "Ao no Hako",
    tmdbId: "262596",
    posterPath: "/j1zidhOBnPpB4axbCSIDQTWoNKN.jpg",
  },
  {
    title: "[Mikanani] Re:从零开始的异世界生活 第三季 - 16 (1080p)",
    desc: "昴在异世界的又一次轮回",
    season: 3,
    episode: 16,
    animeName: "Re:从零开始的异世界生活",
    originalName: "Re:Zero",
    tmdbId: "65006",
    posterPath: "/4Dub8EWkEJiVkQduXyRuuspbAEh.jpg",
  },
  {
    title: "[ANi] 魔法少女毁灭者 - 12 (1080p)",
    desc: "以暴力手段对抗魔法少女",
    season: 1,
    episode: 12,
  },
  {
    title: "[SubsPlease] 怪兽8号 / Kaiju No. 8 - 12 (1080p)",
    desc: "日比野卡夫卡的怪兽之力",
    season: 1,
    episode: 12,
    animeName: "怪兽8号",
    originalName: "Kaiju No. 8",
    tmdbId: "237091",
    posterPath: "/9K5DISM3MpCpIYI6VwVoaQOxKlV.jpg",
  },
  {
    title: "[Mikanani] 天国大魔境 / Tengoku Daimakyou - 13 (1080p)",
    desc: "废墟日本的末日生存之旅",
    season: 1,
    episode: 13,
    animeName: "天国大魔境",
    originalName: "Tengoku Daimakyou",
    tmdbId: "198225",
    posterPath: "/rHzZqeAunqRZnRDnjkKvMN9VXaA.jpg",
  },
  {
    title: "[ANi] 摇曳露营 第三季 - 12 (1080p)",
    desc: "志摩凛与各务原抚子的户外露营日常",
    season: 3,
    episode: 12,
    animeName: "摇曳露营",
    originalName: "Yuru Camp",
    tmdbId: "73042",
    posterPath: "/kLBltKJYY9kReQIaGwZRmC3rJxo.jpg",
  },
  {
    title: "[SubsPlease] 排球少年 垃圾场决战 (1080p)",
    desc: "音驹 vs 乌野的巅峰之战",
    season: 4,
    episode: null,
    animeName: "排球少年",
    originalName: "Haikyuu!!",
    tmdbId: "60863",
    posterPath: "/4bHCJxrpNaGNiCJwSzdAlGhkidb.jpg",
  },
  {
    title: "[Mikanani] 地错 第五季 - 12 (1080p)",
    desc: "贝尔在地下城寻求邂逅",
    season: 5,
    episode: 12,
    animeName: "在地下城寻求邂逅是否搞错了什么",
    originalName: "DanMachi",
    tmdbId: "62745",
    posterPath: "/7HtvMBN0Z8dsnvKDh72iAfyD9XF.jpg",
  },
  {
    title: "[ANi] 樱子小姐的脚下埋着尸体 - 12 (1080p)",
    desc: "九条樱子的骸骨推理",
    season: 1,
    episode: 12,
  },
  {
    title: "[SubsPlease] 异世界自杀小队 - 10 (1080p)",
    desc: "DC反派们的异世界冒险",
    season: 1,
    episode: 10,
  },
  {
    title: "[Mikanani] 葬送的芙莉莲 / Sousou no Frieren - 27 (1080p)",
    desc: "勇者一行的魔法使芙莉莲的旅途故事",
    season: 1,
    episode: 27,
    animeName: "葬送的芙莉莲",
    originalName: "Sousou no Frieren",
    tmdbId: "209867",
    posterPath: "/dqZENchTd7lp5zht7BdlqM7RBhD.jpg",
  },
  {
    title: "[Mikanani] 葬送的芙莉莲 / Sousou no Frieren - 26 (1080p)",
    desc: "勇者一行的魔法使芙莉莲的旅途故事",
    season: 1,
    episode: 26,
    animeName: "葬送的芙莉莲",
    originalName: "Sousou no Frieren",
    tmdbId: "209867",
    posterPath: "/dqZENchTd7lp5zht7BdlqM7RBhD.jpg",
  },
  {
    title: "[SubsPlease] 鬼灭之刃 柱训练篇 - 07 (1080p)",
    desc: "炭治郎与柱们的训练篇章",
    season: 4,
    episode: 7,
    animeName: "鬼灭之刃",
    originalName: "Kimetsu no Yaiba",
    tmdbId: "85937",
    posterPath: "/wrC2TWAOPQMD4bpGjdwH7MjjPT3.jpg",
  },
  {
    title: "[ANi] 间谍过家家 第三季 - 11 (1080p)",
    desc: "黄昏一家的间谍喜剧日常",
    season: 3,
    episode: 11,
    animeName: "间谍过家家",
    originalName: "SPY×FAMILY",
    tmdbId: "110248",
    posterPath: "/3bWEMlYABPXCNYBZhcjyxLKoRBL.jpg",
  },
  {
    title: "[LoliHouse] 葬送的芙莉莲 / Sousou no Frieren - 28 (1080p)",
    desc: "勇者一行的魔法使芙莉莲的旅途故事",
    season: 1,
    episode: 28,
    animeName: "葬送的芙莉莲",
    originalName: "Sousou no Frieren",
    tmdbId: "209867",
    posterPath: "/dqZENchTd7lp5zht7BdlqM7RBhD.jpg",
  },
  {
    title: "[ANi] 葬送的芙莉莲 / Sousou no Frieren - 28 (1080p)",
    desc: "勇者一行的魔法使芙莉莲的旅途故事",
    season: 1,
    episode: 28,
    animeName: "葬送的芙莉莲",
    originalName: "Sousou no Frieren",
    tmdbId: "209867",
    posterPath: "/dqZENchTd7lp5zht7BdlqM7RBhD.jpg",
  },
];

/** @type {Map<string, object>} */
const animations = new Map();
const downloadState = new Map(); // id -> { state, progress, startedAt }

function initAnimations() {
  const now = Date.now();
  ANIME_TITLES.forEach((entry, i) => {
    const id = randomUUID();
    const publishTime = new Date(now - i * 3600_000 * 6).toISOString();

    let isDownloadTracked = false;
    let isDownloadFinished = false;

    if (i < 3 || i === 17 || i === 21) {
      // First 3 plus one multi-episode item: finished
      isDownloadTracked = true;
      isDownloadFinished = true;
    } else if (i < 5) {
      // Next 2: downloading
      isDownloadTracked = true;
      downloadState.set(id, {
        state: "Downloading",
        progress: 0.1 + Math.random() * 0.5,
        startedAt: now,
      });
    } else if (i === 5) {
      // One paused
      isDownloadTracked = true;
      downloadState.set(id, {
        state: "Paused",
        progress: 0.35,
        startedAt: now,
      });
    }
    // Rest: untracked

    animations.set(id, {
      id,
      title: entry.title,
      description: entry.desc,
      publishTime,
      isDownloadTracked,
      isDownloadFinished,
      releaseSizeBytes: (420 + (i % 8) * 115) * 1024 * 1024,
      automationDisposition:
        i === 4
          ? "AutoDownloadQueued"
          : i === 6
            ? "PendingConfirmation"
            : i === 7
              ? "Notified"
              : i === 8
                ? "AutoDownloadFailed"
                : null,
      season: entry.season,
      episode: entry.episode,
      group: { name: entry.title.match(/\[(.+?)\]/)?.[1] ?? "Fansub" },
      animation: entry.animeName
        ? {
            tmdbId: entry.tmdbId,
            name: entry.animeName,
            originalName: entry.originalName ?? "",
            posterPath: entry.posterPath ?? null,
          }
        : null,
      isAiProcessed: !!entry.animeName,
      isMediaLibraryImport: i === 2,
    });
  });
}

initAnimations();

// Metadata review queue. Preview objects are short-lived and bind an edited
// metadata snapshot to the item's current revision, just like the real API.
const metadataReviewItems = new Map();
const metadataReviewPreviews = new Map();
const metadataReviewOperations = [];
const metadataCatalog = new Map(
  ANIME_TITLES.filter((entry) => entry.tmdbId).map((entry) => [
    entry.tmdbId,
    {
      tmdbId: entry.tmdbId,
      name: entry.animeName,
      originalName: entry.originalName ?? null,
      posterPath: entry.posterPath ?? null,
    },
  ]),
);

function mockMappedFiles(animation, index) {
  if (!animation.isDownloadFinished) return [];
  const name = animation.animation?.name ?? "unknown";
  const groupName = animation.group?.name ?? "Unknown";
  const season = String(animation.season ?? 1).padStart(2, "0");
  const episode = String(animation.episode ?? 1).padStart(2, "0");
  const base = `${name} S${season}E${episode}`;
  const root = `/${name}/${groupName}`;
  const files = [
    {
      fileName: `${base}.mkv`,
      currentVirtualPath: `${root}/${base}.mkv`,
    },
  ];
  if (index === 0 || index === 2) {
    files.push({
      fileName: `${base}.zh.srt`,
      currentVirtualPath: `${root}/${base}.zh.srt`,
    });
  }
  if (index === 17) {
    files.push(
      {
        fileName: `${base}-02.mkv`,
        currentVirtualPath: `${root}/${base}-02.mkv`,
      },
      {
        fileName: `${base}-03.mkv`,
        currentVirtualPath: `${root}/${base}-03.mkv`,
      },
    );
  }
  return files;
}

function initMetadataReviewItems() {
  const queueSeeds = [
    { index: 0, status: "lowConfidence", confidence: 0.42 },
    { index: 1, status: "lowConfidence", confidence: 0.56 },
    { index: 2, status: "pending", confidence: null },
    { index: 3, status: "lowConfidence", confidence: 0.48 },
    { index: 4, status: "pending", confidence: null },
    { index: 5, status: "failed", confidence: null },
    { index: 6, status: "lowConfidence", confidence: 0.61 },
    { index: 7, status: "pending", confidence: null },
    { index: 13, status: "failed", confidence: null },
    { index: 17, status: "failed", confidence: null },
    { index: 19, status: "failed", confidence: null },
    { index: 20, status: "pending", confidence: null },
  ];
  const values = [...animations.values()];

  for (const seed of queueSeeds) {
    const animation = values[seed.index];
    if (!animation) continue;
    const forceUnresolved =
      seed.status === "pending" ||
      (seed.status === "failed" && animation.animation == null);
    const files = mockMappedFiles(animation, seed.index);
    metadataReviewItems.set(animation.id, {
      id: animation.id,
      title: animation.title,
      description: animation.description ?? null,
      publishTime: animation.publishTime,
      reviewStatus: seed.status,
      confidence: seed.confidence,
      failureReason:
        seed.status === "failed"
          ? seed.index === 17
            ? "AI response did not contain a usable episode number."
            : "Metadata inference failed after the configured retry limit."
          : null,
      aiRetryCount:
        seed.status === "failed" ? 3 : seed.status === "pending" ? 0 : 1,
      metadata: {
        tmdbId: forceUnresolved ? null : (animation.animation?.tmdbId ?? null),
        name: forceUnresolved ? null : (animation.animation?.name ?? null),
        originalName: forceUnresolved
          ? null
          : (animation.animation?.originalName ?? null),
        posterPath: forceUnresolved
          ? null
          : (animation.animation?.posterPath ?? null),
        season: forceUnresolved ? null : (animation.season ?? null),
        episode: forceUnresolved ? null : (animation.episode ?? null),
        groupName: animation.group?.name ?? null,
      },
      isDownloadFinished: animation.isDownloadFinished,
      mappedFileCount: files.length,
      revision: 1,
      currentOperationId: null,
      files,
    });
  }
}

initMetadataReviewItems();

function publicMetadataReviewItem(item) {
  const { files: _files, ...publicItem } = item;
  return publicItem;
}

function publicMetadataReviewOperation(operation) {
  return {
    operationId: operation.operationId,
    itemId: operation.itemId,
    title: operation.title,
    appliedAt: operation.appliedAt,
    revision: operation.revision,
    canUndo: operation.canUndo,
  };
}

function sanitizePathSegment(value) {
  return (
    String(value)
      .replace(/[\\/:*?"<>|]/g, "_")
      .trim() || "Unknown"
  );
}

function extensionForMockFile(fileName) {
  const subtitle = fileName.match(/(\.[a-z]{2,3})?\.(srt|ass|vtt)$/i);
  if (subtitle) return `${subtitle[1] ?? ""}.${subtitle[2]}`;
  const dot = fileName.lastIndexOf(".");
  return dot >= 0 ? fileName.slice(dot) : "";
}

function buildMetadataPathChanges(item, metadata) {
  if (item.files.length === 0) return [];
  if (!metadata.name || metadata.season == null) {
    return item.files.map((file) => ({
      fileName: file.fileName,
      currentVirtualPath: file.currentVirtualPath,
      proposedVirtualPath: null,
      changeKind: "removed",
      collisionAdjusted: false,
    }));
  }

  const name = sanitizePathSegment(metadata.name);
  const group = sanitizePathSegment(metadata.groupName ?? "Unknown");
  const season = String(metadata.season).padStart(2, "0");
  const episode =
    metadata.episode == null ? null : String(metadata.episode).padStart(2, "0");

  return item.files.map((file, index) => {
    const episodeSuffix = episode == null ? "" : `E${episode}`;
    const extension = extensionForMockFile(file.fileName);
    const sequenceSuffix =
      item.files.length > 2 && index > 0
        ? `-${String(index + 1).padStart(2, "0")}`
        : "";
    const baseName = `${name} S${season}${episodeSuffix}${sequenceSuffix}`;
    const collisionAdjusted =
      index === 0 && metadata.groupName?.toLowerCase() === "collision";
    const adjustedBase = collisionAdjusted ? `${baseName} (2)` : baseName;
    const proposedVirtualPath = `/${name}/${group}/${adjustedBase}${extension}`;
    return {
      fileName: file.fileName,
      currentVirtualPath: file.currentVirtualPath,
      proposedVirtualPath,
      changeKind:
        file.currentVirtualPath === proposedVirtualPath ? "unchanged" : "moved",
      collisionAdjusted,
    };
  });
}

// Advance download progress every second
setInterval(() => {
  for (const [id, ds] of downloadState) {
    if (ds.state === "Downloading") {
      ds.progress = Math.min(1, ds.progress + 0.02 + Math.random() * 0.01);
      if (ds.progress >= 1) {
        ds.state = "Finished";
        ds.progress = 1;
        const anim = animations.get(id);
        if (anim) {
          anim.isDownloadFinished = true;
          if (
            anim.automationDisposition === "AutoDownloadQueued" ||
            anim.automationDisposition === "ManualDownloadQueued"
          ) {
            anim.automationDisposition = "DownloadCompleted";
          }
        }
      }
    }
  }
}, 1000);

// Mock season bangumi data
const SEASON_BANGUMIS = [
  { mikanId: 3899, title: "尖帽子的魔法工房", dayOfWeek: 1, imageUrl: null },
  {
    mikanId: 3904,
    title: "自称恶役大小姐的婚约者观察记录。",
    dayOfWeek: 1,
    imageUrl: null,
  },
  {
    mikanId: 3850,
    title: "吹响吧！上低音号 第三季",
    dayOfWeek: 2,
    imageUrl: null,
  },
  { mikanId: 3880, title: "怪异与少女与神隐", dayOfWeek: 2, imageUrl: null },
  { mikanId: 3870, title: "暗杀贵族 第二季", dayOfWeek: 3, imageUrl: null },
  {
    mikanId: 3815,
    title: "转生贵族的异世界冒险录 第二季",
    dayOfWeek: 3,
    imageUrl: null,
  },
  { mikanId: 3910, title: "无名记忆 第二季", dayOfWeek: 4, imageUrl: null },
  { mikanId: 3920, title: "我的幸福婚约 第三季", dayOfWeek: 4, imageUrl: null },
  {
    mikanId: 3860,
    title: "关于我转生变成史莱姆这档事 第四季",
    dayOfWeek: 5,
    imageUrl: null,
  },
  { mikanId: 3890, title: "迷宫饭 第二季", dayOfWeek: 5, imageUrl: null },
  { mikanId: 3841, title: "鬼灭之刃 无限城篇", dayOfWeek: 6, imageUrl: null },
  { mikanId: 3900, title: "恋上换装娃娃 第三季", dayOfWeek: 6, imageUrl: null },
  { mikanId: 227, title: "名侦探柯南", dayOfWeek: 0, imageUrl: null },
  { mikanId: 228, title: "航海王", dayOfWeek: 0, imageUrl: null },
  {
    mikanId: 3950,
    title: "剧场版 紫罗兰永恒花园",
    dayOfWeek: 7,
    imageUrl: null,
  },
].map((b) => ({
  ...b,
  id: randomUUID(),
  scrapedAt: new Date(Date.now() - 86400_000).toISOString(),
}));

const MOCK_SUBGROUPS = {
  3899: [
    { mikanSubgroupId: 370, name: "LoliHouse" },
    { mikanSubgroupId: 513, name: "喵萌奶茶屋" },
    { mikanSubgroupId: 202, name: "ANi" },
  ],
  3904: [
    { mikanSubgroupId: 370, name: "LoliHouse" },
    { mikanSubgroupId: 615, name: "桜都字幕组" },
  ],
  3860: [
    { mikanSubgroupId: 370, name: "LoliHouse" },
    { mikanSubgroupId: 513, name: "喵萌奶茶屋" },
    { mikanSubgroupId: 202, name: "ANi" },
    { mikanSubgroupId: 1231, name: "SubsPlease" },
  ],
};

// Feeds
let feeds = [
  {
    id: randomUUID(),
    url: "https://mikanani.me/RSS/Bangumi?bangumiId=3141",
    name: "葬送的芙莉莲",
    createdAt: new Date(Date.now() - 86400_000 * 3).toISOString(),
  },
  {
    id: randomUUID(),
    url: "https://mikanani.me/RSS/Bangumi?bangumiId=3143",
    name: "迷宫饭",
    createdAt: new Date(Date.now() - 86400_000 * 2).toISOString(),
  },
  {
    id: randomUUID(),
    url: "https://mikanani.me/RSS/Bangumi?bangumiId=3200",
    name: "药屋少女的呢喃",
    createdAt: new Date(Date.now() - 86400_000).toISOString(),
  },
];

// Per-feed subscription automation policies and historical releases.
const POLICY_CREATED_AT = new Date(Date.now() - 86400_000).toISOString();
const subscriptionPolicies = new Map([
  [
    feeds[0].id,
    {
      feedId: feeds[0].id,
      subtitleGroups: ["LoliHouse", "喵萌奶茶屋"],
      resolutions: ["1080p"],
      codecs: ["HEVC"],
      languages: ["简中", "繁中"],
      minSizeBytes: 300 * 1024 * 1024,
      maxSizeBytes: 1600 * 1024 * 1024,
      excludedKeywords: ["合集", "NCOP"],
      mode: "ManualConfirm",
      enableVersionUpgrade: true,
      minimumUpgradeScore: 80,
      upgradeRollbackHours: 72,
      createdAt: POLICY_CREATED_AT,
      updatedAt: new Date(Date.now() - 3600_000 * 8).toISOString(),
    },
  ],
  [
    feeds[1].id,
    {
      feedId: feeds[1].id,
      subtitleGroups: ["ANi", "SubsPlease"],
      resolutions: ["1080p"],
      codecs: [],
      languages: ["繁中"],
      minSizeBytes: null,
      maxSizeBytes: 1400 * 1024 * 1024,
      excludedKeywords: ["预告"],
      mode: "AutoDownload",
      enableVersionUpgrade: false,
      minimumUpgradeScore: 25,
      upgradeRollbackHours: 72,
      createdAt: POLICY_CREATED_AT,
      updatedAt: new Date(Date.now() - 3600_000 * 3).toISOString(),
    },
  ],
]);

const RELEASE_HISTORY_BY_FEED = new Map([
  [
    feeds[0].id,
    [
      {
        id: randomUUID(),
        title:
          "[LoliHouse] 葬送的芙莉莲 - 28 [WebRip 1080p HEVC-10bit AAC][简繁内封]",
        publishedAt: new Date(Date.now() - 3600_000 * 5).toISOString(),
        sizeBytes: 824 * 1024 * 1024,
        subtitleGroup: "LoliHouse",
        resolution: "1080p",
        codec: "HEVC",
        languages: ["简中", "繁中"],
      },
      {
        id: randomUUID(),
        title: "[ANi] 葬送的芙莉莲 - 28 [1080P][繁日双语]",
        publishedAt: new Date(Date.now() - 3600_000 * 12).toISOString(),
        sizeBytes: 516 * 1024 * 1024,
        subtitleGroup: "ANi",
        resolution: "1080p",
        codec: "AVC",
        languages: ["繁中", "日语"],
      },
      {
        id: randomUUID(),
        title: "[喵萌奶茶屋] 葬送的芙莉莲 01-28 合集 [1080p HEVC][简繁]",
        publishedAt: new Date(Date.now() - 86400_000).toISOString(),
        sizeBytes: 18.4 * 1024 * 1024 * 1024,
        subtitleGroup: "喵萌奶茶屋",
        resolution: "1080p",
        codec: "HEVC",
        languages: ["简中", "繁中"],
      },
      {
        id: randomUUID(),
        title: "[LoliHouse] 葬送的芙莉莲 - 27 [2160p HEVC][简繁]",
        publishedAt: new Date(Date.now() - 86400_000 * 2).toISOString(),
        sizeBytes: 2250 * 1024 * 1024,
        subtitleGroup: "LoliHouse",
        resolution: "2160p",
        codec: "HEVC",
        languages: ["简中", "繁中"],
      },
    ],
  ],
  [
    feeds[1].id,
    [
      {
        id: randomUUID(),
        title: "[ANi] 迷宫饭 - 24 [1080P][繁日双语]",
        publishedAt: new Date(Date.now() - 3600_000 * 7).toISOString(),
        sizeBytes: 612 * 1024 * 1024,
        subtitleGroup: "ANi",
        resolution: "1080p",
        codec: "AVC",
        languages: ["繁中", "日语"],
      },
      {
        id: randomUUID(),
        title: "[SubsPlease] Dungeon Meshi - 24 (1080p) [English]",
        publishedAt: new Date(Date.now() - 3600_000 * 18).toISOString(),
        sizeBytes: 1380 * 1024 * 1024,
        subtitleGroup: "SubsPlease",
        resolution: "1080p",
        codec: "AVC",
        languages: ["English"],
      },
      {
        id: randomUUID(),
        title: "[ANi] 迷宫饭 完结纪念预告 [1080P][繁中]",
        publishedAt: new Date(Date.now() - 86400_000 * 2).toISOString(),
        sizeBytes: 92 * 1024 * 1024,
        subtitleGroup: "ANi",
        resolution: "1080p",
        codec: "AVC",
        languages: ["繁中"],
      },
    ],
  ],
  [
    feeds[2].id,
    [
      {
        id: randomUUID(),
        title: "[LoliHouse] 药屋少女的呢喃 - 24 [WebRip 1080p HEVC][简繁]",
        publishedAt: new Date(Date.now() - 3600_000 * 10).toISOString(),
        sizeBytes: 745 * 1024 * 1024,
        subtitleGroup: "LoliHouse",
        resolution: "1080p",
        codec: "HEVC",
        languages: ["简中", "繁中"],
      },
      {
        id: randomUUID(),
        title: "[ANi] 药屋少女的呢喃 - 24 [720P][繁中]",
        publishedAt: new Date(Date.now() - 86400_000).toISOString(),
        sizeBytes: 324 * 1024 * 1024,
        subtitleGroup: "ANi",
        resolution: "720p",
        codec: "AVC",
        languages: ["繁中"],
      },
    ],
  ],
]);

function simulatePolicy(feedId, policy) {
  const history = RELEASE_HISTORY_BY_FEED.get(feedId) ?? [];
  const formatBytes = (bytes) => {
    const units = ["B", "KiB", "MiB", "GiB", "TiB"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return `${Number(value.toFixed(2))} ${units[unit]} (${Math.round(bytes)} bytes)`;
  };
  const normalizeAllowedValue = (field, value) => {
    let normalized = value.trim().toUpperCase();
    if (field === "resolution") {
      normalized = normalized.replace(/\s/g, "");
      const aliases = {
        "4K": "2160P",
        UHD: "2160P",
        2160: "2160P",
        1440: "1440P",
        FHD: "1080P",
        1080: "1080P",
        HD: "720P",
        720: "720P",
        576: "576P",
        480: "480P",
      };
      return aliases[normalized] ?? normalized;
    }
    if (field === "codec") {
      normalized = normalized.replace(/[.\-\s]/g, "");
      const aliases = {
        H265: "HEVC",
        X265: "HEVC",
        H264: "AVC",
        X264: "AVC",
      };
      return aliases[normalized] ?? normalized;
    }
    if (field === "languages") {
      normalized = normalized.replace(/[_\-\s]/g, "");
      const aliases = {
        CHS: "ZHHANS",
        SC: "ZHHANS",
        GB: "ZHHANS",
        ZHCN: "ZHHANS",
        简体: "ZHHANS",
        简中: "ZHHANS",
        簡中: "ZHHANS",
        简体中文: "ZHHANS",
        CHT: "ZHHANT",
        TC: "ZHHANT",
        BIG5: "ZHHANT",
        ZHTW: "ZHHANT",
        ZHHK: "ZHHANT",
        繁体: "ZHHANT",
        繁體: "ZHHANT",
        繁中: "ZHHANT",
        繁體中文: "ZHHANT",
        JPN: "JA",
        JAP: "JA",
        日语: "JA",
        日語: "JA",
        日本語: "JA",
        JAPANESE: "JA",
        ENG: "EN",
        英语: "EN",
        英語: "EN",
        ENGLISH: "EN",
      };
      return aliases[normalized] ?? normalized;
    }
    return normalized;
  };
  const checkAllowed = (field, actualValues, expectedValues) => {
    const actual = actualValues.filter(Boolean);
    const expected = (expectedValues ?? []).filter(Boolean);
    if (expected.length === 0) {
      return {
        field,
        passed: true,
        actual: actual.join(", ") || null,
        expected: null,
        message: "anyValueAllowed",
      };
    }
    const normalizedExpected = new Set(
      expected.map((value) => normalizeAllowedValue(field, value)),
    );
    const passed = actual.some((value) =>
      normalizedExpected.has(normalizeAllowedValue(field, value)),
    );
    return {
      field,
      passed,
      actual: actual.join(", ") || null,
      expected: expected.join(", "),
      message: passed ? "allowedValueMatched" : "allowedValueMissed",
    };
  };

  const entries = history.map((item) => {
    const explanations = [
      checkAllowed(
        "subtitleGroup",
        [item.subtitleGroup],
        policy.subtitleGroups,
      ),
      checkAllowed("resolution", [item.resolution], policy.resolutions),
      checkAllowed("codec", [item.codec], policy.codecs),
      checkAllowed("languages", item.languages, policy.languages),
    ];
    const min =
      typeof policy.minSizeBytes === "number" ? policy.minSizeBytes : null;
    const max =
      typeof policy.maxSizeBytes === "number" ? policy.maxSizeBytes : null;
    const sizePassed =
      (min == null || item.sizeBytes >= min) &&
      (max == null || item.sizeBytes <= max);
    explanations.push({
      field: "size",
      passed: sizePassed,
      actual: formatBytes(item.sizeBytes),
      expected:
        min == null && max == null
          ? null
          : `${min == null ? "0 B" : formatBytes(min)} – ${max == null ? "∞" : formatBytes(max)}`,
      message: sizePassed ? "withinSizeRange" : "outsideSizeRange",
    });
    const excluded = (policy.excludedKeywords ?? []).filter(Boolean);
    const found = excluded.find((keyword) =>
      item.title.toLowerCase().includes(keyword.toLowerCase()),
    );
    explanations.push({
      field: "excludedKeywords",
      passed: !found,
      actual: found ?? null,
      expected: excluded.length > 0 ? excluded.join(", ") : null,
      message: found ? "excludedKeywordFound" : "noExcludedKeyword",
    });
    return {
      id: item.id,
      title: item.title,
      publishedAt: item.publishedAt,
      sizeBytes: item.sizeBytes,
      matched: explanations.every((reason) => reason.passed),
      explanations,
    };
  });

  return {
    total: entries.length,
    matched: entries.filter((entry) => entry.matched).length,
    entries,
  };
}

// WebDAV access tokens
let webDavTokens = [
  {
    id: randomUUID(),
    username: "sdw-demo01",
    description: "客厅 Mac mini",
    createdAt: new Date(Date.now() - 86400_000).toISOString(),
  },
];

// Runtime-editable settings. The mock exposes only secret state, never the
// plaintext, just like the real API.
let systemSettings = {
  revision: 1,
  pendingRestart: false,
  ai: {
    executionMode: "builtIn",
    provider: "openAI",
    openAI: {
      baseUrl: "https://api.openai.com/v1",
      apiMode: "responses",
      model: "gpt-4o-mini",
      maxTokens: 1024,
      apiKey: { isConfigured: true, source: "deployment" },
    },
    anthropic: {
      baseUrl: "https://api.anthropic.com",
      model: "claude-sonnet-4-20250514",
      maxTokens: 1024,
      apiVersion: "2023-06-01",
      apiKey: { isConfigured: false, source: "none" },
    },
    codexAppServer: {
      endpoint: "ws://127.0.0.1:4500",
      model: "",
      permissionProfile: ":read-only",
      timeoutSeconds: 120,
      token: { isConfigured: false, source: "none" },
    },
    inference: { rateLimitDelayMs: 1000 },
  },
  tmdb: { apiKey: { isConfigured: true, source: "deployment" } },
  torrent: {
    url: "http://localhost:8080",
    userName: "",
    userAgent: "",
    password: { isConfigured: false, source: "none" },
  },
  mediaLibrary: {
    allowedRoots: ["/media"],
    scanInterval: "00:05:00",
    settlingPeriod: "00:00:30",
    missingGracePeriod: "1.00:00:00",
  },
  incidents: {
    downloadStalledAfter: "00:15:00",
    reportThrottle: "00:05:00",
    reconciliationInterval: "00:05:00",
    disk: {
      minimumAvailableBytes: 5 * 1024 * 1024 * 1024,
      minimumAvailablePercent: 5,
    },
  },
  nfs: {
    enabled: false,
    port: 2049,
    bindAddress: "127.0.0.1",
    leaseSeconds: 90,
    maxConnections: 32,
    idleTimeoutSeconds: 120,
    allowAnonymous: false,
    allowedNetworks: ["127.0.0.0/8", "::1/128"],
    restartRequired: true,
    pendingRestart: false,
  },
  notifications: {
    webhookEnabled: false,
    webPushEnabled: false,
    webPushSubject: "",
    vapidPublicKey: "",
    vapidPrivateKey: { isConfigured: false, source: "none" },
    events: [
      "releaseMatched",
      "downloadPendingConfirmation",
      "downloadCompleted",
      "downloadFailed",
      "incidentOpened",
      "metadataNeedsReview",
      "diskSpaceLow",
    ],
    quietHoursStart: null,
    quietHoursEnd: null,
    timeZoneId: "UTC",
    webhookUrl: { isConfigured: false, source: "none" },
  },
};

const deploymentSecrets = {
  openAi: { isConfigured: true, source: "deployment" },
  anthropic: { isConfigured: false, source: "none" },
  codex: { isConfigured: false, source: "none" },
  tmdb: { isConfigured: true, source: "deployment" },
  torrent: { isConfigured: false, source: "none" },
  webhook: { isConfigured: false, source: "none" },
};

let notificationDeliveries = [];
let webPushSubscriptions = [];
const mockVapidPublicKey =
  "BGb1EKTo02dge1GKm7kU8hSQowk4T8Qnpl8dOB1nrnSQJnrhc6OdQ3a4gtyGTkera6bMWIp9cKAlEdN_BA6gGQM";

function applySecretMutation(current, mutation, deploymentValue) {
  if (!mutation || mutation.operation === "keep") return current;
  if (mutation.operation === "set") {
    if (typeof mutation.value !== "string" || !mutation.value.trim())
      throw new Error("A non-empty secret value is required");
    return { isConfigured: true, source: "runtime" };
  }
  if (mutation.operation === "clear")
    return { isConfigured: false, source: "runtime" };
  if (mutation.operation === "reset") return { ...deploymentValue };
  throw new Error("Unknown secret operation");
}

function endpointOrigin(value) {
  try {
    return new URL(value).origin;
  } catch {
    return null;
  }
}

function requiresCredentialMutation(currentUrl, nextUrl, secret, mutation) {
  return (
    secret.isConfigured &&
    endpointOrigin(currentUrl) !== endpointOrigin(nextUrl) &&
    !["set", "clear"].includes(mutation?.operation)
  );
}

function isMockAiConfigured() {
  const ai = systemSettings.ai;
  if (ai.executionMode === "codexAppServer")
    return Boolean(
      ai.codexAppServer.endpoint && ai.codexAppServer.permissionProfile,
    );
  const provider = ai.provider === "openAI" ? ai.openAI : ai.anthropic;
  return provider.apiKey.isConfigured && Boolean(provider.model);
}

// Existing media-library import sources. Scans are asynchronous so the
// Settings page can exercise the same polling flow as the real API.
let mediaLibrarySources = [
  {
    id: randomUUID(),
    path: "/media/anime",
    isMonitoring: true,
    createdAt: new Date(Date.now() - 86400_000 * 7).toISOString(),
    lastScanAt: new Date(Date.now() - 3600_000 * 2).toISOString(),
    lastError: null,
    lastImportedCount: 18,
    lastUpdatedCount: 2,
    lastRemovedCount: 1,
    lastSkippedCount: 4,
    isScanning: false,
  },
];

function isAbsoluteServerPath(path) {
  return (
    path.startsWith("/") ||
    /^[A-Za-z]:[\\/]/.test(path) ||
    path.startsWith("\\\\")
  );
}

function startMediaLibraryScan(source) {
  if (source.isScanning) return false;

  const isFirstScan = source.lastScanAt == null;
  source.isScanning = true;
  source.lastError = null;

  setTimeout(() => {
    source.isScanning = false;
    source.lastScanAt = new Date().toISOString();
    source.lastImportedCount = isFirstScan ? 12 : 1;
    source.lastUpdatedCount = isFirstScan ? 0 : 2;
    source.lastRemovedCount = isFirstScan ? 0 : 1;
    source.lastSkippedCount = isFirstScan ? 3 : 14;
  }, 1200);

  return true;
}

// Mock file tree
const FILE_TREE = {
  "": [
    { fileName: "Season 1", isDirectory: true, relative: "Season 1" },
    { fileName: "Season 2", isDirectory: true, relative: "Season 2" },
    { fileName: "Specials", isDirectory: true, relative: "Specials" },
  ],
  "Season 1": [
    ...Array.from({ length: 12 }, (_, i) => ({
      fileName: `EP${String(i + 1).padStart(2, "0")}.mp4`,
      isDirectory: false,
      relative: null,
    })),
    {
      fileName: "EP01.zh-Hans.srt",
      isDirectory: false,
      relative: null,
    },
    { fileName: "EP01.en.srt", isDirectory: false, relative: null },
  ],
  "Season 2": Array.from({ length: 12 }, (_, i) => ({
    fileName: `EP${String(i + 1).padStart(2, "0")}.mp4`,
    isDirectory: false,
    relative: null,
  })),
  Specials: [
    { fileName: "OVA01.mp4", isDirectory: false, relative: null },
    { fileName: "OVA02.mp4", isDirectory: false, relative: null },
    { fileName: "NCOP.mp4", isDirectory: false, relative: null },
    { fileName: "NCED.mp4", isDirectory: false, relative: null },
  ],
};

// Playback state is user-scoped in the real API. The mock server has one user,
// so a composite animation/path key is sufficient for cross-page persistence.
const playbackProgress = new Map();
let playbackPreferences = {
  subtitleLanguage: "zh",
  subtitleTrackLabel: null,
  audioLanguage: "ja",
  audioTrackLabel: null,
  autoPlayNext: true,
  updatedAt: new Date().toISOString(),
};

function playbackKey(animationInfoId, path) {
  return `${animationInfoId}:${path}`;
}

function playablePaths() {
  const paths = [];
  for (const [directory, entries] of Object.entries(FILE_TREE)) {
    for (const entry of entries) {
      if (
        !entry.isDirectory &&
        /\.(mkv|mp4|webm|avi|flv|wmv|mov|m4v|ts|m2ts)$/i.test(entry.fileName)
      ) {
        paths.push(
          directory ? `${directory}/${entry.fileName}` : entry.fileName,
        );
      }
    }
  }
  return paths;
}

function playbackVirtualPath(animation, path) {
  const name = sanitizePathSegment(animation.animation?.name ?? "unknown");
  const group = sanitizePathSegment(animation.group?.name ?? "Unknown");
  return animation.animation && animation.season != null
    ? `/${name}/${group}/${path}`
    : `/unknown/${path}`;
}

function playbackMedia(animation, path) {
  return {
    animationInfoId: animation.id,
    path,
    virtualPath: playbackVirtualPath(animation, path),
    title: animation.title,
    animationName: animation.animation?.name ?? null,
    posterPath: animation.animation?.posterPath ?? null,
    season: animation.season ?? null,
    episode: animation.episode ?? null,
  };
}

function playbackState(animation, path, stored) {
  return {
    animationInfoId: animation.id,
    path,
    virtualPath: playbackVirtualPath(animation, path),
    positionSeconds: stored?.positionSeconds ?? 0,
    durationSeconds: stored?.durationSeconds ?? 0,
    isWatched: stored?.isWatched ?? false,
    updatedAt: stored?.updatedAt ?? null,
    watchedAt: stored?.watchedAt ?? null,
  };
}

function findNextPlaybackMedia(animation) {
  if (!animation.animation?.tmdbId || animation.episode == null) return null;
  const next = [...animations.values()]
    .filter(
      (candidate) =>
        candidate.isDownloadFinished &&
        candidate.animation?.tmdbId === animation.animation.tmdbId &&
        candidate.season === animation.season &&
        candidate.episode != null &&
        candidate.episode > animation.episode,
    )
    .sort((a, b) => a.episode - b.episode)[0];
  return next ? playbackMedia(next, "Season 1/EP01.mp4") : null;
}

function associatedSubtitles(animation, videoPath) {
  const slash = videoPath.lastIndexOf("/");
  const directory = slash >= 0 ? videoPath.slice(0, slash) : "";
  const videoName = slash >= 0 ? videoPath.slice(slash + 1) : videoPath;
  const stem = videoName.replace(/\.[^.]+$/, "");
  const entries = FILE_TREE[directory] ?? [];
  return entries
    .filter(
      (entry) =>
        !entry.isDirectory &&
        /\.(srt|ass|ssa|vtt|sub)$/i.test(entry.fileName) &&
        entry.fileName.toLowerCase().startsWith(stem.toLowerCase()),
    )
    .map((entry) => {
      const path = directory
        ? `${directory}/${entry.fileName}`
        : entry.fileName;
      const language = entry.fileName.includes("zh-Hans")
        ? "zh-Hans"
        : entry.fileName.includes(".en.")
          ? "en"
          : null;
      return {
        path,
        virtualPath: playbackVirtualPath(animation, path),
        language,
        label: entry.fileName,
        format: entry.fileName.split(".").pop().toLowerCase(),
      };
    });
}

const finishedForPlayback = [...animations.values()].filter(
  (animation) => animation.isDownloadFinished,
);
if (finishedForPlayback[0]) {
  const animation = finishedForPlayback[0];
  const path = "Season 1/EP01.mp4";
  playbackProgress.set(playbackKey(animation.id, path), {
    positionSeconds: 812,
    durationSeconds: 1440,
    isWatched: false,
    updatedAt: new Date(Date.now() - 18 * 60_000).toISOString(),
    watchedAt: null,
  });
}
const previousEpisode = finishedForPlayback.find(
  (animation) =>
    animation.animation?.tmdbId === "209867" && animation.episode === 27,
);
if (previousEpisode) {
  const path = "Season 1/EP01.mp4";
  playbackProgress.set(playbackKey(previousEpisode.id, path), {
    positionSeconds: 420,
    durationSeconds: 1440,
    isWatched: false,
    updatedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
    watchedAt: null,
  });
}

let mockIncidents = [
  {
    id: randomUUID(),
    type: "feedFailure",
    severity: "error",
    title: "Mikan RSS returned HTTP 503",
    detail:
      "The feed could not be refreshed during the last three sync attempts.",
    sourceId: feeds[0]?.id ?? null,
    detectedAt: new Date(Date.now() - 42 * 60_000).toISOString(),
    updatedAt: new Date(Date.now() - 12 * 60_000).toISOString(),
    retryCount: 2,
    lastRetryAt: new Date(Date.now() - 12 * 60_000).toISOString(),
    lastRetryError: "Upstream returned 503 Service Unavailable",
    resolvedAt: null,
    canRetry: true,
  },
  {
    id: randomUUID(),
    type: "downloadStalled",
    severity: "warning",
    title: "Download has not progressed for 20 minutes",
    detail:
      "No peers are currently available. Retry will reannounce the torrent.",
    sourceId: finishedForPlayback[1]?.id ?? null,
    detectedAt: new Date(Date.now() - 25 * 60_000).toISOString(),
    updatedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    retryCount: 0,
    lastRetryAt: null,
    lastRetryError: null,
    resolvedAt: null,
    canRetry: true,
  },
  {
    id: randomUUID(),
    type: "aiFailure",
    severity: "error",
    title: "Metadata inference retry limit reached",
    detail: "The model did not return a valid TMDB ID after three attempts.",
    sourceId: [...animations.values()][13]?.id ?? null,
    detectedAt: new Date(Date.now() - 6 * 3600_000).toISOString(),
    updatedAt: new Date(Date.now() - 6 * 3600_000).toISOString(),
    retryCount: 0,
    lastRetryAt: null,
    lastRetryError: null,
    resolvedAt: null,
    canRetry: true,
  },
  {
    id: randomUUID(),
    type: "fileMappingFailure",
    severity: "error",
    title: "Downloaded files could not be mapped",
    detail:
      "The download completed, but no playable video mapping was produced.",
    sourceId: finishedForPlayback[2]?.id ?? null,
    detectedAt: new Date(Date.now() - 2 * 3600_000).toISOString(),
    updatedAt: new Date(Date.now() - 2 * 3600_000).toISOString(),
    retryCount: 1,
    lastRetryAt: new Date(Date.now() - 90 * 60_000).toISOString(),
    lastRetryError: "File store temporarily unavailable",
    resolvedAt: null,
    canRetry: true,
  },
  {
    id: randomUUID(),
    type: "diskSpaceLow",
    severity: "critical",
    title: "Download volume is almost full",
    detail: "Only 3.8 GB remain on the configured file store.",
    sourceId: "local",
    detectedAt: new Date(Date.now() - 75 * 60_000).toISOString(),
    updatedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
    retryCount: 0,
    lastRetryAt: null,
    lastRetryError: null,
    resolvedAt: null,
    canRetry: true,
  },
];

// Mock VFS tree (mirrors what /api/vfs returns — keyed by absolute virtual path)
const VFS_NOW = Date.now();
const vfsDir = (name) => ({
  name,
  isDirectory: true,
  size: null,
  lastModifiedUtc: null,
});
const vfsFile = (name, sizeMb, ageHours) => ({
  name,
  isDirectory: false,
  size: Math.round(sizeMb * 1024 * 1024),
  lastModifiedUtc: new Date(VFS_NOW - ageHours * 3600_000).toISOString(),
});
const VFS_TREE = {
  "/": [
    vfsDir("葬送のフリーレン"),
    vfsDir("呪術廻戦"),
    vfsDir("frieren-beyond-journey-end"),
    vfsDir("unknown"),
  ],
  "/葬送のフリーレン": [vfsDir("SubsPlease")],
  "/葬送のフリーレン/SubsPlease": Array.from({ length: 8 }, (_, i) =>
    vfsFile(
      `葬送のフリーレン S01E${String(i + 1).padStart(2, "0")}.mkv`,
      1280 + i * 12,
      i * 24,
    ),
  ),
  "/呪術廻戦": [vfsDir("ASW"), vfsDir("Erai-raws")],
  "/呪術廻戦/ASW": Array.from({ length: 6 }, (_, i) =>
    vfsFile(
      `Jujutsu Kaisen S02E${String(i + 1).padStart(2, "0")}.mkv`,
      980 + i * 8,
      24 + i * 12,
    ),
  ),
  "/呪術廻戦/Erai-raws": [
    vfsFile("Jujutsu Kaisen NCOP.mkv", 64, 72),
    vfsFile("Jujutsu Kaisen NCED.mkv", 58, 72),
  ],
  "/frieren-beyond-journey-end": [
    vfsFile("Trailer.mp4", 32, 240),
    vfsFile("Cover.jpg", 0.4, 240),
  ],
  "/unknown": [vfsFile("[unsorted] random release.mkv", 700, 6)],
};

function vfsResolve(rawPath) {
  // Returns { entry, isDirectory, parent } or null when missing.
  let p = rawPath || "/";
  if (!p.startsWith("/")) return null;
  if (p.includes("/..") || p.split("/").includes(".")) return null;
  if (p.length > 1) p = p.replace(/\/+$/, "");
  if (p === "/" || VFS_TREE[p]) {
    const name = p === "/" ? "" : p.slice(p.lastIndexOf("/") + 1);
    return {
      entry: { name, isDirectory: true, size: null, lastModifiedUtc: null },
      isDirectory: true,
    };
  }
  // Try as a file: parent's children include the leaf name.
  const lastSlash = p.lastIndexOf("/");
  const parent = lastSlash === 0 ? "/" : p.slice(0, lastSlash);
  const leaf = p.slice(lastSlash + 1);
  const siblings = VFS_TREE[parent];
  if (!siblings) return null;
  const match = siblings.find((e) => e.name === leaf && !e.isDirectory);
  if (!match) return null;
  return { entry: match, isDirectory: false };
}

const mockTodoStates = new Map();

function currentMockTodos() {
  const anime = [...animations.values()];
  const base = [
    anime[0] && {
      key: `automation:${anime[0].id}`,
      type: "ReleaseMatched",
      priority: "Normal",
      title: anime[0].title,
      detail: "A notify-only subscription matched this release.",
      deepLink: `/todo?focus=automation:${anime[0].id}`,
      resourceId: anime[0].id,
      occurredAt: anime[0].publishTime,
    },
    anime[1] && {
      key: `automation:${anime[1].id}`,
      type: "DownloadPendingConfirmation",
      priority: "High",
      title: anime[1].title,
      detail: "A matched release is waiting for download confirmation.",
      deepLink: `/todo?focus=automation:${anime[1].id}`,
      resourceId: anime[1].id,
      occurredAt: anime[1].publishTime,
    },
    ...mockIncidents
      .filter((incident) => !incident.resolvedAt)
      .map((incident) => ({
        key: `incident:${incident.id}`,
        type: incident.type === "diskSpaceLow" ? "DiskSpaceLow" : "Incident",
        priority: incident.severity === "critical" ? "Critical" : "High",
        title: incident.title,
        detail: incident.detail,
        deepLink:
          incident.type === "diskSpaceLow"
            ? "/incidents?type=diskSpaceLow"
            : `/incidents?focus=${incident.id}`,
        resourceId: incident.id,
        occurredAt: incident.detectedAt,
      })),
  ].filter(Boolean);

  return base
    .map((item) => ({
      ...item,
      readAt: mockTodoStates.get(item.key)?.readAt ?? null,
      snoozedUntil: mockTodoStates.get(item.key)?.snoozedUntil ?? null,
    }))
    .sort((left, right) => {
      const rank = { Normal: 0, High: 1, Critical: 2 };
      return (
        rank[right.priority] - rank[left.priority] ||
        new Date(right.occurredAt) - new Date(left.occurredAt)
      );
    });
}

// ---------------------------------------------------------------------------
// Router
// ---------------------------------------------------------------------------

async function route(method, pathname, searchParams, req, res) {
  console.log(
    `${method} ${pathname}${searchParams.toString() ? "?" + searchParams : ""}`,
  );

  // --- CORS preflight ---
  if (method === "OPTIONS") {
    return empty(res, 204);
  }

  // --- Auth ---

  if (method === "GET" && pathname === "/api/auth/allowregister") {
    return json(res, { allow: !registered });
  }

  if (method === "POST" && pathname === "/api/auth/register") {
    registered = true;
    return json(res, issueAuth());
  }

  if (method === "POST" && pathname === "/api/auth/login") {
    if (!registered)
      return json(res, { token: "", refreshToken: "", success: false });
    return json(res, issueAuth());
  }

  if (method === "POST" && pathname === "/api/auth/refresh") {
    const body = await readBody(req);
    if (!activeRefreshTokens.delete(body.refreshToken))
      return json(res, { token: null, refreshToken: null, success: false }, 400);
    return json(res, issueAuth());
  }

  if (method === "POST" && pathname === "/api/auth/logout") {
    const body = await readBody(req);
    activeRefreshTokens.delete(body.refreshToken);
    return empty(res, 204);
  }

  if (method === "GET" && pathname === "/api/auth/verify") {
    if (!hasAuth(req)) return empty(res, 401);
    return json(res, [{ Type: "sub", Value: "mock-user" }]);
  }

  // --- All remaining endpoints require auth ---
  if (!hasAuth(req) && !pathname.startsWith("/api/auth/")) {
    return empty(res, 401);
  }

  if (method === "GET") {
    const posterMatch = pathname.match(
      /^\/api\/images\/tmdb\/(?:w92|w154|w185|w300|w342|w500|w780|original)\/([a-z0-9][a-z0-9._-]{0,199}\.(?:avif|jpe?g|png|webp))$/i,
    );
    if (posterMatch) return mockPoster(res, posterMatch[1]);
  }

  // --- Runtime system settings ---

  if (method === "GET" && pathname === "/api/settings") {
    return json(res, systemSettings);
  }

  if (method === "PATCH" && pathname === "/api/settings") {
    const body = await readBody(req);
    if (body.expectedRevision !== systemSettings.revision)
      return json(res, { error: "Settings revision conflict" }, 409);

    const unsafeCredentialChange =
      (body.ai &&
        (requiresCredentialMutation(
          systemSettings.ai.openAI.baseUrl,
          body.ai.openAI.baseUrl,
          systemSettings.ai.openAI.apiKey,
          body.ai.openAI.apiKey,
        ) ||
          requiresCredentialMutation(
            systemSettings.ai.anthropic.baseUrl,
            body.ai.anthropic.baseUrl,
            systemSettings.ai.anthropic.apiKey,
            body.ai.anthropic.apiKey,
          ) ||
          requiresCredentialMutation(
            systemSettings.ai.codexAppServer.endpoint,
            body.ai.codexAppServer.endpoint,
            systemSettings.ai.codexAppServer.token,
            body.ai.codexAppServer.token,
          ))) ||
      (body.torrent &&
        requiresCredentialMutation(
          systemSettings.torrent.url,
          body.torrent.url,
          systemSettings.torrent.password,
          body.torrent.password,
        ));
    if (unsafeCredentialChange)
      return json(
        res,
        { error: "A credential must be set or cleared after an origin change" },
        400,
      );
    if (body.ai && !body.ai.codexAppServer.permissionProfile?.trim())
      return json(res, { error: "A permission profile is required" }, 400);

    try {
      if (body.ai) {
        systemSettings.ai = {
          executionMode: body.ai.executionMode,
          provider: body.ai.provider,
          openAI: {
            ...body.ai.openAI,
            apiKey: applySecretMutation(
              systemSettings.ai.openAI.apiKey,
              body.ai.openAI.apiKey,
              deploymentSecrets.openAi,
            ),
          },
          anthropic: {
            ...body.ai.anthropic,
            apiKey: applySecretMutation(
              systemSettings.ai.anthropic.apiKey,
              body.ai.anthropic.apiKey,
              deploymentSecrets.anthropic,
            ),
          },
          codexAppServer: {
            ...body.ai.codexAppServer,
            token: applySecretMutation(
              systemSettings.ai.codexAppServer.token,
              body.ai.codexAppServer.token,
              deploymentSecrets.codex,
            ),
          },
          inference: { ...body.ai.inference },
        };
      }

      if (body.tmdb)
        systemSettings.tmdb = {
          apiKey: applySecretMutation(
            systemSettings.tmdb.apiKey,
            body.tmdb.apiKey,
            deploymentSecrets.tmdb,
          ),
        };

      if (body.torrent)
        systemSettings.torrent = {
          url: body.torrent.url,
          userName: body.torrent.userName,
          userAgent: body.torrent.userAgent,
          password: applySecretMutation(
            systemSettings.torrent.password,
            body.torrent.password,
            deploymentSecrets.torrent,
          ),
        };

      if (body.mediaLibrary)
        systemSettings.mediaLibrary = {
          ...body.mediaLibrary,
          allowedRoots: [...body.mediaLibrary.allowedRoots],
        };
      if (body.incidents)
        systemSettings.incidents = {
          ...body.incidents,
          disk: { ...body.incidents.disk },
        };

      if (body.nfs) {
        const runningNfs = {
          enabled: systemSettings.nfs.enabled,
          port: systemSettings.nfs.port,
          bindAddress: systemSettings.nfs.bindAddress,
          leaseSeconds: systemSettings.nfs.leaseSeconds,
          maxConnections: systemSettings.nfs.maxConnections,
          idleTimeoutSeconds: systemSettings.nfs.idleTimeoutSeconds,
          allowAnonymous: systemSettings.nfs.allowAnonymous,
          allowedNetworks: [...systemSettings.nfs.allowedNetworks],
        };
        const changed = JSON.stringify(runningNfs) !== JSON.stringify(body.nfs);
        systemSettings.nfs = {
          ...body.nfs,
          restartRequired: true,
          pendingRestart: systemSettings.nfs.pendingRestart || changed,
        };
        systemSettings.pendingRestart = systemSettings.nfs.pendingRestart;
      }

      if (body.notifications) {
        const generateVapidKeys =
          body.notifications.generateVapidKeys &&
          !systemSettings.notifications.vapidPrivateKey.isConfigured;
        systemSettings.notifications = {
          webhookEnabled: body.notifications.webhookEnabled,
          webPushEnabled: body.notifications.webPushEnabled,
          webPushSubject: body.notifications.webPushSubject,
          vapidPublicKey: generateVapidKeys
            ? mockVapidPublicKey
            : systemSettings.notifications.vapidPublicKey,
          vapidPrivateKey: generateVapidKeys
            ? { isConfigured: true, source: "runtime" }
            : systemSettings.notifications.vapidPrivateKey,
          events: [...body.notifications.events],
          quietHoursStart: body.notifications.quietHoursStart,
          quietHoursEnd: body.notifications.quietHoursEnd,
          timeZoneId: body.notifications.timeZoneId,
          webhookUrl: applySecretMutation(
            systemSettings.notifications.webhookUrl,
            body.notifications.webhookUrl,
            deploymentSecrets.webhook,
          ),
        };
      }

      systemSettings.revision += 1;
      return json(res, systemSettings);
    } catch (error) {
      return json(res, { error: error.message }, 400);
    }
  }

  if (
    method === "GET" &&
    pathname === "/api/notifications/web-push/config"
  ) {
    return json(res, {
      enabled: systemSettings.notifications.webPushEnabled,
      vapidPublicKey: systemSettings.notifications.vapidPublicKey,
    });
  }

  if (
    method === "GET" &&
    pathname === "/api/notifications/web-push/subscriptions"
  ) {
    return json(
      res,
      webPushSubscriptions.map(({ endpoint: _endpoint, ...summary }) => summary),
    );
  }

  if (
    method === "POST" &&
    pathname === "/api/notifications/web-push/subscriptions"
  ) {
    if (!systemSettings.notifications.webPushEnabled)
      return json(res, { message: "Enable Web Push first" }, 409);
    const body = await readBody(req);
    const now = new Date().toISOString();
    let subscription = webPushSubscriptions.find(
      (item) => item.endpoint === body.endpoint,
    );
    if (subscription) {
      subscription.updatedAt = now;
      subscription.lastError = null;
    } else {
      subscription = {
        id: randomUUID(),
        endpoint: body.endpoint,
        endpointOrigin: new URL(body.endpoint).origin,
        endpointHash: createHash("sha256").update(body.endpoint).digest("hex"),
        createdAt: now,
        updatedAt: now,
        lastSuccessAt: null,
        lastFailureAt: null,
        lastError: null,
      };
      webPushSubscriptions.unshift(subscription);
    }
    const { endpoint: _endpoint, ...summary } = subscription;
    return json(res, summary);
  }

  if (
    method === "POST" &&
    pathname ===
      "/api/notifications/web-push/subscriptions/remove-current"
  ) {
    const body = await readBody(req);
    webPushSubscriptions = webPushSubscriptions.filter(
      (item) => item.endpoint !== body.endpoint,
    );
    res.writeHead(204);
    return res.end();
  }

  const webPushDeleteMatch = pathname.match(
    /^\/api\/notifications\/web-push\/subscriptions\/([^/]+)$/,
  );
  if (method === "DELETE" && webPushDeleteMatch) {
    const before = webPushSubscriptions.length;
    webPushSubscriptions = webPushSubscriptions.filter(
      (item) => item.id !== webPushDeleteMatch[1],
    );
    res.writeHead(before === webPushSubscriptions.length ? 404 : 204);
    return res.end();
  }

  if (method === "POST" && pathname === "/api/notifications/test") {
    const webhookReady =
      systemSettings.notifications.webhookEnabled &&
      systemSettings.notifications.webhookUrl.isConfigured;
    const webPushReady =
      systemSettings.notifications.webPushEnabled &&
      webPushSubscriptions.length > 0;
    if (!webhookReady && !webPushReady)
      return json(res, { message: "Configure a destination first" }, 409);
    const eventId = randomUUID();
    const channels = [
      ...(webhookReady ? ["Webhook"] : []),
      ...webPushSubscriptions
        .filter(() => webPushReady)
        .map(() => "WebPush"),
    ];
    notificationDeliveries.unshift(
      ...channels.map((channel, index) => ({
        id: index === 0 ? eventId : randomUUID(),
        eventId,
        channel,
        type: "test",
        status: "Delivered",
        attemptCount: 1,
        occurredAt: new Date().toISOString(),
        lastAttemptAt: new Date().toISOString(),
        deliveredAt: new Date().toISOString(),
        lastError: null,
      })),
    );
    return json(res, { eventId }, 202);
  }

  if (method === "GET" && pathname === "/api/notifications/deliveries") {
    const take = Math.min(
      100,
      Math.max(1, Number(searchParams.get("take")) || 20),
    );
    return json(res, notificationDeliveries.slice(0, take));
  }

  if (method === "GET" && pathname === "/api/todos") {
    const includeRead = searchParams.get("includeRead") === "true";
    const includeSnoozed = searchParams.get("includeSnoozed") === "true";
    const skip = Math.max(0, Number(searchParams.get("skip")) || 0);
    const take = Math.min(
      200,
      Math.max(1, Number(searchParams.get("take")) || 50),
    );
    const focus = searchParams.get("focus");
    const now = Date.now();
    const all = currentMockTodos();
    const unreadCount = all.filter(
      (item) =>
        !item.readAt &&
        (!item.snoozedUntil || new Date(item.snoozedUntil) <= now),
    ).length;
    const visible = all.filter(
      (item) =>
        (includeRead || !item.readAt) &&
        (includeSnoozed ||
          !item.snoozedUntil ||
          new Date(item.snoozedUntil) <= now),
    );
    const items = visible.slice(skip, skip + take);
    const focused = focus && all.find((item) => item.key === focus);
    if (focused && !items.some((item) => item.key === focused.key))
      items.unshift(focused);
    return json(res, { items, totalCount: visible.length, unreadCount });
  }

  if (method === "PATCH" && pathname === "/api/todos/state") {
    const body = await readBody(req);
    const now = new Date().toISOString();
    for (const key of body.keys ?? []) {
      const state = mockTodoStates.get(key) ?? {
        readAt: null,
        snoozedUntil: null,
      };
      if (body.action === "markRead") state.readAt = now;
      if (body.action === "markUnread") state.readAt = null;
      if (body.action === "snooze") state.snoozedUntil = body.snoozedUntil;
      if (body.action === "unsnooze") state.snoozedUntil = null;
      mockTodoStates.set(key, state);
    }
    return empty(res, 204);
  }

  // --- Playback continuity ---

  if (method === "GET" && pathname === "/api/playback/continue") {
    const limit = Math.min(
      100,
      Math.max(1, parseInt(searchParams.get("limit") ?? "20", 10) || 20),
    );
    const items = [];
    for (const [key, stored] of playbackProgress) {
      if (stored.isWatched || stored.positionSeconds <= 0) continue;
      const separator = key.indexOf(":");
      const animationInfoId = key.slice(0, separator);
      const path = key.slice(separator + 1);
      const animation = animations.get(animationInfoId);
      if (!animation || !animation.isDownloadFinished) continue;
      items.push({
        media: playbackMedia(animation, path),
        state: playbackState(animation, path, stored),
      });
    }
    items.sort(
      (a, b) =>
        new Date(b.state.updatedAt).getTime() -
        new Date(a.state.updatedAt).getTime(),
    );
    return json(res, items.slice(0, limit));
  }

  if (method === "GET" && pathname === "/api/playback/states") {
    const animation = animations.get(searchParams.get("animationInfoId"));
    if (!animation) return empty(res, 404);
    return json(
      res,
      playablePaths().map((path) =>
        playbackState(
          animation,
          path,
          playbackProgress.get(playbackKey(animation.id, path)),
        ),
      ),
    );
  }

  if (method === "GET" && pathname === "/api/playback/context") {
    const animation = animations.get(searchParams.get("animationInfoId"));
    const path = searchParams.get("path");
    if (
      !animation ||
      !animation.isDownloadFinished ||
      !playablePaths().includes(path)
    ) {
      return empty(res, 404);
    }
    return json(res, {
      media: playbackMedia(animation, path),
      state: playbackProgress.has(playbackKey(animation.id, path))
        ? playbackState(
            animation,
            path,
            playbackProgress.get(playbackKey(animation.id, path)),
          )
        : null,
      preferences: playbackPreferences,
      subtitles: associatedSubtitles(animation, path),
      next: findNextPlaybackMedia(animation),
    });
  }

  if (method === "PUT" && pathname === "/api/playback/progress") {
    const body = await readBody(req);
    const animation = animations.get(body.animationInfoId);
    if (!animation || !playablePaths().includes(body.path))
      return empty(res, 404);
    const positionSeconds = Math.max(0, Number(body.positionSeconds) || 0);
    const durationSeconds = Math.max(0, Number(body.durationSeconds) || 0);
    const key = playbackKey(animation.id, body.path);
    const previous = playbackProgress.get(key);
    const isWatched =
      previous?.isWatched ||
      (durationSeconds > 0 && positionSeconds / durationSeconds >= 0.9);
    const updatedAt = new Date().toISOString();
    const stored = {
      positionSeconds: Math.min(
        positionSeconds,
        durationSeconds || positionSeconds,
      ),
      durationSeconds,
      isWatched,
      updatedAt,
      watchedAt: isWatched ? (previous?.watchedAt ?? updatedAt) : null,
    };
    playbackProgress.set(key, stored);
    return json(res, playbackState(animation, body.path, stored));
  }

  if (method === "PUT" && pathname === "/api/playback/watched") {
    const body = await readBody(req);
    const animation = animations.get(body.animationInfoId);
    if (!animation || !playablePaths().includes(body.path))
      return empty(res, 404);
    const key = playbackKey(animation.id, body.path);
    const previous = playbackProgress.get(key) ?? {
      positionSeconds: 0,
      durationSeconds: 0,
    };
    const updatedAt = new Date().toISOString();
    const stored = {
      ...previous,
      isWatched: !!body.isWatched,
      updatedAt,
      watchedAt: body.isWatched ? updatedAt : null,
    };
    playbackProgress.set(key, stored);
    return json(res, playbackState(animation, body.path, stored));
  }

  if (method === "GET" && pathname === "/api/playback/preferences") {
    return json(res, playbackPreferences);
  }

  if (method === "PUT" && pathname === "/api/playback/preferences") {
    const body = await readBody(req);
    playbackPreferences = {
      subtitleLanguage: body.subtitleLanguage ?? null,
      subtitleTrackLabel: body.subtitleTrackLabel ?? null,
      audioLanguage: body.audioLanguage ?? null,
      audioTrackLabel: body.audioTrackLabel ?? null,
      autoPlayNext: body.autoPlayNext !== false,
      updatedAt: new Date().toISOString(),
    };
    return json(res, playbackPreferences);
  }

  // --- Incident inbox ---

  if (method === "GET" && pathname === "/api/incidents") {
    const type = searchParams.get("type");
    const includeResolved = searchParams.get("includeResolved") === "true";
    const skip = Math.max(
      0,
      parseInt(searchParams.get("skip") ?? "0", 10) || 0,
    );
    const take = Math.min(
      200,
      Math.max(1, parseInt(searchParams.get("take") ?? "50", 10) || 50),
    );
    const openItems = mockIncidents.filter((incident) => !incident.resolvedAt);
    const countsByType = Object.fromEntries(
      [
        "feedFailure",
        "downloadStalled",
        "aiFailure",
        "fileMappingFailure",
        "diskSpaceLow",
      ].map((incidentType) => [
        incidentType,
        openItems.filter((incident) => incident.type === incidentType).length,
      ]),
    );
    const filtered = mockIncidents
      .filter((incident) => !type || incident.type === type)
      .filter((incident) => includeResolved || !incident.resolvedAt)
      .sort((a, b) => new Date(b.updatedAt) - new Date(a.updatedAt));
    return json(res, {
      items: filtered.slice(skip, skip + take),
      totalCount: filtered.length,
      openCount: openItems.length,
      countsByType,
    });
  }

  if (method === "POST" && pathname === "/api/incidents/retry-all") {
    const results = [];
    for (const incident of mockIncidents.filter((item) => !item.resolvedAt)) {
      incident.retryCount += 1;
      incident.lastRetryAt = new Date().toISOString();
      incident.updatedAt = incident.lastRetryAt;
      const success = incident.type !== "diskSpaceLow";
      if (success) {
        incident.resolvedAt = incident.lastRetryAt;
        incident.lastRetryError = null;
        incident.canRetry = false;
      } else {
        incident.lastRetryError =
          "Free space is still below the configured threshold";
      }
      results.push({
        incidentId: incident.id,
        success,
        error: success ? null : incident.lastRetryError,
      });
    }
    return json(res, {
      attempted: results.length,
      succeeded: results.filter((result) => result.success).length,
      failed: results.filter((result) => !result.success).length,
      results,
    });
  }

  {
    const match = pathname.match(/^\/api\/incidents\/([^/]+)\/retry$/);
    if (method === "POST" && match) {
      const incident = mockIncidents.find((item) => item.id === match[1]);
      if (!incident) return empty(res, 404);
      if (incident.resolvedAt)
        return json(res, { error: "Already resolved" }, 409);
      incident.retryCount += 1;
      incident.lastRetryAt = new Date().toISOString();
      incident.updatedAt = incident.lastRetryAt;
      if (incident.type === "diskSpaceLow") {
        incident.lastRetryError =
          "Free space is still below the configured threshold";
        return json(
          res,
          {
            incidentId: incident.id,
            success: false,
            error: incident.lastRetryError,
          },
          422,
        );
      }
      incident.resolvedAt = incident.lastRetryAt;
      incident.lastRetryError = null;
      incident.canRetry = false;
      return json(res, incident);
    }
  }

  // --- Metadata review ---

  if (method === "GET" && pathname === "/api/metadata-review") {
    const status = searchParams.get("status") ?? "pending";
    if (!["pending", "lowConfidence", "failed"].includes(status)) {
      return json(res, { error: "Unsupported review status." }, 422);
    }
    const skip = Math.max(
      0,
      parseInt(searchParams.get("skip") ?? "0", 10) || 0,
    );
    const take = Math.min(
      100,
      Math.max(1, parseInt(searchParams.get("take") ?? "20", 10) || 20),
    );
    const all = [...metadataReviewItems.values()];
    const filtered = all
      .filter((item) => item.reviewStatus === status)
      .sort((a, b) => new Date(b.publishTime) - new Date(a.publishTime));
    const counts = {
      pending: all.filter((item) => item.reviewStatus === "pending").length,
      lowConfidence: all.filter((item) => item.reviewStatus === "lowConfidence")
        .length,
      failed: all.filter((item) => item.reviewStatus === "failed").length,
    };
    const recentOperations = metadataReviewOperations
      .slice(0, 10)
      .map(publicMetadataReviewOperation);

    return json(res, {
      data: filtered.slice(skip, skip + take).map(publicMetadataReviewItem),
      totalItems: filtered.length,
      counts,
      recentOperations,
    });
  }

  // POST /api/metadata-review/:id/preview
  {
    const match = pathname.match(/^\/api\/metadata-review\/([^/]+)\/preview$/);
    if (method === "POST" && match) {
      const item = metadataReviewItems.get(match[1]);
      if (!item) return empty(res, 404);
      const body = await readBody(req);
      if (body.expectedRevision !== item.revision) {
        return json(res, { error: "Revision conflict." }, 409);
      }

      const edited = body.metadata;
      const validTmdbId =
        edited &&
        typeof edited.tmdbId === "string" &&
        /^\d+$/.test(edited.tmdbId) &&
        Number(edited.tmdbId) > 0 &&
        Number(edited.tmdbId) <= 2_147_483_647;
      const validIndex = (value) =>
        value === null || (Number.isSafeInteger(value) && value >= 0);
      const validGroup =
        edited &&
        (edited.groupName === null ||
          (typeof edited.groupName === "string" &&
            edited.groupName.length <= 200));
      if (
        !edited ||
        !validTmdbId ||
        edited.season == null ||
        !validIndex(edited.season) ||
        !validIndex(edited.episode) ||
        !validGroup
      ) {
        return json(res, { error: "Invalid metadata fields." }, 422);
      }

      const warnings = [];
      const catalogEntry = edited.tmdbId
        ? metadataCatalog.get(edited.tmdbId)
        : null;
      let identity;
      if (catalogEntry) {
        identity = catalogEntry;
      } else {
        identity = {
          tmdbId: edited.tmdbId,
          name: `TMDB ${edited.tmdbId}`,
          originalName: null,
          posterPath: null,
        };
        warnings.push(
          "The mock catalog does not contain this TMDB ID; a placeholder title will be used.",
        );
      }

      const resolvedMetadata = {
        ...identity,
        season: edited.season,
        episode: edited.episode,
        groupName: edited.groupName?.trim() || null,
      };
      if (resolvedMetadata.episode == null) {
        warnings.push(
          "No episode was set; downloaded files will use a season-level filename.",
        );
      }
      if (!item.isDownloadFinished) {
        warnings.push("notDownloaded");
      }

      const pathChanges = buildMetadataPathChanges(item, resolvedMetadata);
      const preview = {
        previewId: randomUUID(),
        itemId: item.id,
        baseRevision: item.revision,
        resolvedMetadata,
        pathChanges,
        warnings,
        canApply: true,
        expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
      };
      metadataReviewPreviews.set(preview.previewId, preview);
      return json(res, {
        previewId: preview.previewId,
        baseRevision: preview.baseRevision,
        resolvedMetadata: preview.resolvedMetadata,
        pathChanges: preview.pathChanges,
        warnings: preview.warnings,
        canApply: preview.canApply,
        expiresAt: preview.expiresAt,
      });
    }
  }

  // POST /api/metadata-review/:id/apply
  {
    const match = pathname.match(/^\/api\/metadata-review\/([^/]+)\/apply$/);
    if (method === "POST" && match) {
      const item = metadataReviewItems.get(match[1]);
      if (!item) return empty(res, 404);
      const body = await readBody(req);
      const preview = metadataReviewPreviews.get(body.previewId);
      if (!preview || preview.itemId !== item.id) {
        return json(res, { error: "Unknown preview." }, 422);
      }
      if (
        Date.parse(preview.expiresAt) <= Date.now() ||
        preview.baseRevision !== item.revision
      ) {
        metadataReviewPreviews.delete(preview.previewId);
        return json(res, { error: "Preview is stale." }, 409);
      }
      if (!preview.canApply) {
        return json(res, { error: "Preview cannot be applied." }, 422);
      }

      if (item.currentOperationId) {
        const current = metadataReviewOperations.find(
          (operation) => operation.operationId === item.currentOperationId,
        );
        if (current) current.canUndo = false;
      }

      const previous = {
        metadata: structuredClone(item.metadata),
        reviewStatus: item.reviewStatus,
        confidence: item.confidence,
        failureReason: item.failureReason,
        files: structuredClone(item.files),
        mappedFileCount: item.mappedFileCount,
        currentOperationId: item.currentOperationId,
      };
      const operationId = randomUUID();
      const appliedAt = new Date().toISOString();

      item.metadata = structuredClone(preview.resolvedMetadata);
      item.reviewStatus = "reviewed";
      item.confidence = 1;
      item.failureReason = null;
      item.revision += 1;
      item.currentOperationId = operationId;
      item.files = preview.pathChanges
        .filter((change) => change.proposedVirtualPath != null)
        .map((change) => ({
          fileName: change.fileName,
          currentVirtualPath: change.proposedVirtualPath,
        }));
      item.mappedFileCount = item.files.length;

      const operation = {
        operationId,
        itemId: item.id,
        title: item.title,
        appliedAt,
        revision: item.revision,
        canUndo: true,
        previous,
        pathChanges: structuredClone(preview.pathChanges),
      };
      metadataReviewOperations.unshift(operation);
      metadataReviewPreviews.delete(preview.previewId);

      return json(res, {
        operationId,
        revision: item.revision,
        pathChanges: preview.pathChanges,
        appliedAt,
        canUndo: true,
      });
    }
  }

  // POST /api/metadata-review/remaps/:operationId/undo
  {
    const match = pathname.match(
      /^\/api\/metadata-review\/remaps\/([^/]+)\/undo$/,
    );
    if (method === "POST" && match) {
      const operation = metadataReviewOperations.find(
        (candidate) => candidate.operationId === match[1],
      );
      if (!operation) return empty(res, 404);
      if (!operation.canUndo) {
        return json(res, { error: "Operation is no longer undoable." }, 422);
      }
      const item = metadataReviewItems.get(operation.itemId);
      if (!item) return empty(res, 404);
      const body = await readBody(req);
      if (
        body.expectedRevision !== item.revision ||
        item.currentOperationId !== operation.operationId
      ) {
        return json(res, { error: "Revision conflict." }, 409);
      }

      const reverseKind = {
        added: "removed",
        removed: "added",
        moved: "moved",
        unchanged: "unchanged",
      };
      const pathChanges = operation.pathChanges.map((change) => ({
        fileName: change.fileName,
        currentVirtualPath: change.proposedVirtualPath,
        proposedVirtualPath: change.currentVirtualPath,
        changeKind: reverseKind[change.changeKind],
        collisionAdjusted: false,
      }));

      item.metadata = structuredClone(operation.previous.metadata);
      item.reviewStatus = operation.previous.reviewStatus;
      item.confidence = operation.previous.confidence;
      item.failureReason = operation.previous.failureReason;
      item.files = structuredClone(operation.previous.files);
      item.mappedFileCount = operation.previous.mappedFileCount;
      item.revision += 1;
      item.currentOperationId = operation.previous.currentOperationId;
      operation.canUndo = false;
      operation.revision = item.revision;
      const appliedAt = new Date().toISOString();

      return json(res, {
        operationId: operation.operationId,
        revision: item.revision,
        pathChanges,
        appliedAt,
        canUndo: false,
      });
    }
  }

  // --- Animation Info ---

  if (method === "GET" && pathname === "/api/library/search") {
    const q = (searchParams.get("q") ?? "").toLocaleLowerCase();
    const season = searchParams.get("season");
    const episode = searchParams.get("episode");
    const source = searchParams.get("source") ?? "Any";
    const downloadState = searchParams.get("downloadState") ?? "Any";
    const resolution = searchParams.get("resolution");
    const codec = searchParams.get("codec");
    const pathQuery = (searchParams.get("path") ?? "").toLocaleLowerCase();
    const take = Math.min(
      100,
      Math.max(1, Number(searchParams.get("take") ?? 30)),
    );
    let offset = 0;
    try {
      if (searchParams.get("cursor"))
        offset =
          Number(
            Buffer.from(searchParams.get("cursor"), "base64url").toString(
              "utf8",
            ),
          ) || 0;
    } catch {}

    const mapped = [...animations.values()]
      .map((item, index) => {
        const group = item.group?.name ?? null;
        const itemResolution = /2160p/i.test(item.title) ? "2160p" : "1080p";
        const itemCodec = /HEVC/i.test(item.title) ? "HEVC" : "AVC";
        const name = item.animation?.name ?? item.title;
        const virtualPaths = item.isDownloadFinished
          ? [
              `/${name}/${group ?? "Unknown"}/${name} S${String(item.season ?? 1).padStart(2, "0")}E${String(item.episode ?? 1).padStart(2, "0")}.mkv`,
            ]
          : [];
        return {
          animationInfoId: item.id,
          title: item.title,
          animationName: item.animation?.name ?? null,
          animationOriginalName: item.animation?.originalName ?? null,
          tmdbId: item.animation?.tmdbId ?? null,
          season: item.season,
          episode: item.episode,
          subtitleGroup: group,
          resolution: itemResolution,
          codec: itemCodec,
          languages: index % 2 ? ["ja"] : ["zh-CN"],
          isDownloadTracked: item.isDownloadTracked,
          isDownloadFinished: item.isDownloadFinished,
          isMediaLibraryImport: item.isMediaLibraryImport,
          isWatched: false,
          playbackPositionSeconds: null,
          virtualPaths,
          virtualPathCount: virtualPaths.length,
          releaseScore: 260 + (index % 5) * 55,
          scoreReasons: [
            `resolution:${itemResolution}:+200`,
            `codec:${itemCodec}:+40`,
          ],
          publishedAt: item.publishTime,
        };
      })
      .filter((item) => {
        const haystack = [
          item.title,
          item.animationName,
          item.animationOriginalName,
          item.tmdbId,
          item.subtitleGroup,
          ...item.virtualPaths,
        ]
          .filter(Boolean)
          .join(" ")
          .toLocaleLowerCase();
        if (q && !haystack.includes(q)) return false;
        if (season && item.season !== Number(season)) return false;
        if (episode && item.episode !== Number(episode)) return false;
        if (source === "MediaLibraryImport" && !item.isMediaLibraryImport)
          return false;
        if (source === "Torrent" && item.isMediaLibraryImport) return false;
        if (downloadState === "Downloaded" && !item.isDownloadFinished)
          return false;
        if (
          downloadState === "Downloading" &&
          (!item.isDownloadTracked || item.isDownloadFinished)
        )
          return false;
        if (downloadState === "NotDownloaded" && item.isDownloadTracked)
          return false;
        if (
          resolution &&
          item.resolution.toLocaleLowerCase() !== resolution.toLocaleLowerCase()
        )
          return false;
        if (
          codec &&
          item.codec.toLocaleLowerCase() !== codec.toLocaleLowerCase()
        )
          return false;
        if (
          pathQuery &&
          !item.virtualPaths.some((path) =>
            path.toLocaleLowerCase().includes(pathQuery),
          )
        )
          return false;
        return true;
      });
    const items = mapped.slice(offset, offset + take);
    const nextCursor =
      offset + take < mapped.length
        ? Buffer.from(String(offset + take)).toString("base64url")
        : null;
    return json(res, { items, nextCursor });
  }

  if (method === "GET" && pathname === "/api/library/integrity") {
    const values = [...animations.values()];
    const current = values[0];
    const candidate = values[21] ?? values[1];
    return json(res, [
      {
        tmdbId: current.animation?.tmdbId ?? "209867",
        animationName: current.animation?.name ?? "葬送的芙莉莲",
        season: 1,
        expectedEpisodeCount: 28,
        missingEpisodes: [25],
        duplicateEpisodes: [
          { episode: 28, releaseIds: [current.id, candidate.id] },
        ],
        unidentifiedReleaseCount: 1,
        upgradeCandidates: [
          {
            currentReleaseId: current.id,
            candidateReleaseId: candidate.id,
            animationName: current.animation?.name ?? "葬送的芙莉莲",
            season: 1,
            episode: 28,
            currentScore: 300,
            candidateScore: 480,
            scoreReasons: ["resolution:2160p:+400", "codec:AV1:+80"],
            automatic: true,
          },
        ],
      },
    ]);
  }

  if (method === "POST" && pathname === "/api/library/upgrades/execute") {
    const body = await readBody(req);
    return json(res, {
      isSuccess: true,
      outcome: body.dryRun ? "ready" : "download_queued",
      dryRun: !!body.dryRun,
      requiresDownload: true,
      operation: body.dryRun ? null : { id: randomUUID() },
      validationErrors: [],
    });
  }

  if (method === "GET" && pathname === "/api/animationinfo") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const all = [...animations.values()];
    return json(res, {
      data: all.slice(skip, skip + take),
      totalItems: all.length,
    });
  }

  if (method === "GET" && pathname === "/api/animationinfo/grouped") {
    const all = [...animations.values()];
    const grouped = new Map();
    for (const item of all) {
      if (item.animation && item.animation.tmdbId) {
        const key = item.animation.tmdbId;
        if (!grouped.has(key)) {
          grouped.set(key, {
            tmdbId: item.animation.tmdbId,
            name: item.animation.name,
            originalName: item.animation.originalName,
            posterPath: item.animation.posterPath ?? null,
            episodes: [],
          });
        }
        grouped.get(key).episodes.push(item);
      }
    }
    const items = [...grouped.values()]
      .map((g) => {
        g.episodes.sort(
          (a, b) =>
            new Date(b.publishTime).getTime() -
              new Date(a.publishTime).getTime() || b.id.localeCompare(a.id),
        );
        const episodeCount = new Set(
          g.episodes
            .filter((episode) => episode.episode != null)
            .map((episode) => `${episode.season ?? ""}:${episode.episode}`),
        ).size;
        return {
          tmdbId: g.tmdbId,
          name: g.name,
          originalName: g.originalName,
          posterPath: g.posterPath,
          episodeCount,
          releaseCount: g.episodes.length,
          automationAttentionCount: g.episodes.filter((episode) =>
            ["Notified", "PendingConfirmation", "AutoDownloadFailed"].includes(
              episode.automationDisposition ?? "",
            ),
          ).length,
          latestPublishTime: g.episodes[0].publishTime,
        };
      })
      .sort(
        (a, b) =>
          new Date(b.latestPublishTime).getTime() -
            new Date(a.latestPublishTime).getTime() ||
          b.tmdbId.localeCompare(a.tmdbId),
      );
    const take = parseInt(searchParams.get("take") ?? "24", 10);
    const offset = parseInt(searchParams.get("cursor") ?? "0", 10);
    return json(res, {
      items: items.slice(offset, offset + take),
      nextCursor: offset + take < items.length ? String(offset + take) : null,
    });
  }

  if (method === "GET" && pathname === "/api/animationinfo/uncategorized") {
    const items = [...animations.values()]
      .filter((item) => !item.animation?.tmdbId)
      .sort(
        (a, b) =>
          new Date(b.publishTime).getTime() -
            new Date(a.publishTime).getTime() || b.id.localeCompare(a.id),
      );
    const take = parseInt(searchParams.get("take") ?? "24", 10);
    const offset = parseInt(searchParams.get("cursor") ?? "0", 10);
    return json(res, {
      items: items.slice(offset, offset + take),
      nextCursor: offset + take < items.length ? String(offset + take) : null,
    });
  }

  const episodeCatalogMatch = pathname.match(
    /^\/api\/animationinfo\/grouped\/([^/]+)\/episodes$/,
  );
  if (method === "GET" && episodeCatalogMatch) {
    const tmdbId = decodeURIComponent(episodeCatalogMatch[1]);
    const episodes = [...animations.values()]
      .filter((item) => item.animation?.tmdbId === tmdbId)
      .sort(
        (a, b) =>
          new Date(b.publishTime).getTime() -
            new Date(a.publishTime).getTime() || b.id.localeCompare(a.id),
      );
    if (episodes.length === 0) return json(res, {}, 404);
    const take = parseInt(searchParams.get("take") ?? "50", 10);
    const offset = parseInt(searchParams.get("cursor") ?? "0", 10);
    const episodeCount = new Set(
      episodes
        .filter((episode) => episode.episode != null)
        .map((episode) => `${episode.season ?? ""}:${episode.episode}`),
    ).size;
    const first = episodes[0];
    return json(res, {
      animation: {
        ...first.animation,
        episodeCount,
        releaseCount: episodes.length,
        automationAttentionCount: episodes.filter((episode) =>
          ["Notified", "PendingConfirmation", "AutoDownloadFailed"].includes(
            episode.automationDisposition ?? "",
          ),
        ).length,
        latestPublishTime: first.publishTime,
      },
      episodes: episodes.slice(offset, offset + take),
      nextCursor:
        offset + take < episodes.length ? String(offset + take) : null,
    });
  }

  if (method === "GET" && pathname === "/api/animationinfo/downloading") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const list = [...animations.values()].filter(
      (a) => a.isDownloadTracked && !a.isDownloadFinished,
    );
    return json(res, {
      data: list.slice(skip, skip + take),
      totalItems: list.length,
    });
  }

  if (method === "GET" && pathname === "/api/animationinfo/downloaded") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const list = [...animations.values()].filter((a) => a.isDownloadFinished);
    return json(res, {
      data: list.slice(skip, skip + take),
      totalItems: list.length,
    });
  }

  // GET /api/animationinfo/status/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/status\/(.+)$/);
    if (method === "GET" && m) {
      const id = m[1];
      const ds = downloadState.get(id);
      if (!ds) return empty(res, 404);
      const speed =
        ds.state === "Downloading" ? 2_500_000 + Math.random() * 5_000_000 : 0;
      const remaining =
        ds.state === "Downloading" && ds.progress > 0
          ? (1 - ds.progress) / 0.02 // ~seconds left
          : 0;
      return json(res, {
        itemId: id,
        progress: ds.progress,
        remaining,
        speed,
        state: ds.state,
      });
    }
  }

  // POST /api/animationinfo/download/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/download\/(.+)$/);
    if (method === "POST" && m) {
      const id = m[1];
      const anim = animations.get(id);
      if (!anim) return empty(res, 404);
      if (anim.isDownloadTracked) return empty(res, 409);
      anim.isDownloadTracked = true;
      anim.isDownloadFinished = false;
      if (
        [
          "Notified",
          "PendingConfirmation",
          "AutoDownloadFailed",
          "DownloadCancelled",
        ].includes(anim.automationDisposition)
      ) {
        anim.automationDisposition = "ManualDownloadQueued";
      }
      downloadState.set(id, {
        state: "Downloading",
        progress: 0,
        startedAt: Date.now(),
      });
      return empty(res, 200);
    }
  }

  // POST /api/animationinfo/pause/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/pause\/(.+)$/);
    if (method === "POST" && m) {
      const ds = downloadState.get(m[1]);
      if (!ds) return empty(res, 404);
      ds.state = "Paused";
      return empty(res, 200);
    }
  }

  // POST /api/animationinfo/resume/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/resume\/(.+)$/);
    if (method === "POST" && m) {
      const ds = downloadState.get(m[1]);
      if (!ds) return empty(res, 404);
      ds.state = "Downloading";
      return empty(res, 200);
    }
  }

  // DELETE /api/animationinfo/cancel/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/cancel\/(.+)$/);
    if (method === "DELETE" && m) {
      const id = m[1];
      const anim = animations.get(id);
      if (!anim) return empty(res, 404);
      if (!anim.isDownloadTracked) return empty(res, 409);
      anim.isDownloadTracked = false;
      anim.isDownloadFinished = false;
      if (
        anim.automationDisposition === "AutoDownloadQueued" ||
        anim.automationDisposition === "ManualDownloadQueued" ||
        anim.automationDisposition === "DownloadCompleted"
      ) {
        anim.automationDisposition = "DownloadCancelled";
      }
      downloadState.delete(id);
      return empty(res, 200);
    }
  }

  // POST /api/animationinfo/:id/retry-inference
  {
    const m = pathname.match(/^\/api\/animationinfo\/(.+)\/retry-inference$/);
    if (method === "POST" && m) {
      const id = m[1];
      const anim = animations.get(id);
      if (!anim) return empty(res, 404);
      anim.isAiProcessed = false;
      console.log(`  Mock: retry inference for '${anim.title}'`);
      return empty(res, 200);
    }
  }

  // POST /api/animationinfo/:id/reidentify-files/ai
  {
    const m = pathname.match(
      /^\/api\/animationinfo\/(.+)\/reidentify-files\/ai$/,
    );
    if (method === "POST" && m) {
      const id = m[1];
      const anim = animations.get(id);
      if (!anim) return empty(res, 404);
      if (
        !anim.isDownloadFinished ||
        anim.animation == null ||
        anim.season == null ||
        anim.episode != null
      ) {
        return empty(res, 409);
      }
      console.log(`  Mock: AI filename re-identification for '${anim.title}'`);
      return empty(res, 200);
    }
  }

  // --- Season Bangumi ---

  if (method === "GET" && pathname === "/api/season") {
    const year = searchParams.get("year");
    const season = searchParams.get("season");
    const seasonLabels = { 春: "春季", 夏: "夏季", 秋: "秋季", 冬: "冬季" };

    if (year && season) {
      // Return fewer mock entries for non-current seasons
      const mockOther = SEASON_BANGUMIS.slice(0, 8).map((b, i) => ({
        ...b,
        id: randomUUID(),
        title: `[${year}${seasonLabels[season] ?? season}] ${b.title}`,
        scrapedAt: new Date().toISOString(),
      }));
      return json(res, {
        year: parseInt(year, 10),
        season,
        lastScrapedAt: new Date().toISOString(),
        bangumis: mockOther,
      });
    }

    const lastScrapedAt =
      SEASON_BANGUMIS.length > 0 ? SEASON_BANGUMIS[0].scrapedAt : null;
    return json(res, {
      year: null,
      season: null,
      lastScrapedAt,
      bangumis: SEASON_BANGUMIS,
    });
  }

  if (method === "POST" && pathname === "/api/season/refresh") {
    const lastScrapedAt =
      SEASON_BANGUMIS.length > 0 ? SEASON_BANGUMIS[0].scrapedAt : null;
    return json(res, { lastScrapedAt, bangumis: SEASON_BANGUMIS });
  }

  // GET /api/season/:mikanId/subgroups
  {
    const m = pathname.match(/^\/api\/season\/(\d+)\/subgroups$/);
    if (method === "GET" && m) {
      const mikanId = parseInt(m[1], 10);
      const subgroups = (
        MOCK_SUBGROUPS[mikanId] ?? [
          { mikanSubgroupId: 370, name: "LoliHouse" },
          { mikanSubgroupId: 202, name: "ANi" },
        ]
      ).map((sg) => ({
        ...sg,
        rssUrl: `https://mikanani.me/RSS/Bangumi?bangumiId=${mikanId}&subgroupid=${sg.mikanSubgroupId}`,
      }));
      return json(res, subgroups);
    }
  }

  // POST /api/season/subscribe
  if (method === "POST" && pathname === "/api/season/subscribe") {
    return readBody(req).then((body) => {
      const mikanId = body.mikanId;
      const subgroupId = body.subgroupId;
      let rssUrl = `https://mikanani.me/RSS/Bangumi?bangumiId=${mikanId}`;
      if (subgroupId != null) rssUrl += `&subgroupid=${subgroupId}`;

      // Check duplicate
      if (feeds.some((f) => f.url === rssUrl)) {
        return json(res, { message: "Already subscribed" }, 409);
      }

      const bangumi = SEASON_BANGUMIS.find((b) => b.mikanId === mikanId);
      let feedName = bangumi?.title ?? `Bangumi ${mikanId}`;
      if (subgroupId != null) {
        const sgs = MOCK_SUBGROUPS[mikanId] ?? [];
        const sg = sgs.find((s) => s.mikanSubgroupId === subgroupId);
        if (sg) feedName = `${feedName} - ${sg.name}`;
      }

      const feed = {
        id: randomUUID(),
        url: rssUrl,
        name: feedName,
        createdAt: new Date().toISOString(),
      };
      feeds.unshift(feed);
      return json(res, feed);
    });
  }

  // --- Feeds ---

  if (method === "GET" && pathname === "/api/feed") {
    return json(res, feeds);
  }

  if (method === "POST" && pathname === "/api/feed") {
    return readBody(req).then((body) => {
      const feed = {
        id: randomUUID(),
        url: body.url ?? "",
        name: body.name || undefined,
        createdAt: new Date().toISOString(),
      };
      feeds.unshift(feed);
      return json(res, feed, 201);
    });
  }

  // DELETE /api/feed/:id
  {
    const m = pathname.match(/^\/api\/feed\/(.+)$/);
    if (method === "DELETE" && m) {
      const before = feeds.length;
      feeds = feeds.filter((f) => f.id !== m[1]);
      subscriptionPolicies.delete(m[1]);
      return empty(res, feeds.length < before ? 200 : 404);
    }
  }

  // --- Subscription automation policies ---

  if (method === "GET" && pathname === "/api/subscription-policies") {
    return json(res, [...subscriptionPolicies.values()]);
  }

  // POST /api/subscription-policies/:feedId/simulate
  {
    const m = pathname.match(
      /^\/api\/subscription-policies\/([^/]+)\/simulate$/,
    );
    if (method === "POST" && m) {
      const feedId = decodeURIComponent(m[1]);
      if (!feeds.some((feed) => feed.id === feedId)) return empty(res, 404);
      return readBody(req).then((body) =>
        json(res, simulatePolicy(feedId, body)),
      );
    }
  }

  // GET/PUT/DELETE /api/subscription-policies/:feedId
  {
    const m = pathname.match(/^\/api\/subscription-policies\/([^/]+)$/);
    if (m) {
      const feedId = decodeURIComponent(m[1]);
      if (!feeds.some((feed) => feed.id === feedId)) return empty(res, 404);

      if (method === "GET") {
        const policy = subscriptionPolicies.get(feedId);
        return policy ? json(res, policy) : empty(res, 404);
      }

      if (method === "PUT") {
        return readBody(req).then((body) => {
          const existing = subscriptionPolicies.get(feedId);
          const policy = {
            feedId,
            subtitleGroups: Array.isArray(body.subtitleGroups)
              ? body.subtitleGroups
              : [],
            resolutions: Array.isArray(body.resolutions)
              ? body.resolutions
              : [],
            codecs: Array.isArray(body.codecs) ? body.codecs : [],
            languages: Array.isArray(body.languages) ? body.languages : [],
            minSizeBytes:
              typeof body.minSizeBytes === "number" ? body.minSizeBytes : null,
            maxSizeBytes:
              typeof body.maxSizeBytes === "number" ? body.maxSizeBytes : null,
            excludedKeywords: Array.isArray(body.excludedKeywords)
              ? body.excludedKeywords
              : [],
            mode: ["NotifyOnly", "ManualConfirm", "AutoDownload"].includes(
              body.mode,
            )
              ? body.mode
              : "ManualConfirm",
            enableVersionUpgrade: !!body.enableVersionUpgrade,
            minimumUpgradeScore: Number.isInteger(body.minimumUpgradeScore)
              ? body.minimumUpgradeScore
              : 25,
            upgradeRollbackHours: Number.isInteger(body.upgradeRollbackHours)
              ? body.upgradeRollbackHours
              : 72,
            createdAt: existing?.createdAt ?? new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          };
          if (
            policy.minSizeBytes != null &&
            policy.maxSizeBytes != null &&
            policy.minSizeBytes > policy.maxSizeBytes
          ) {
            return json(res, { error: "Invalid size range" }, 400);
          }
          subscriptionPolicies.set(feedId, policy);
          return json(res, policy);
        });
      }

      if (method === "DELETE") {
        return empty(res, subscriptionPolicies.delete(feedId) ? 204 : 404);
      }
    }
  }

  // --- WebDAV access tokens ---

  if (method === "GET" && pathname === "/api/webdav-tokens") {
    return json(res, webDavTokens);
  }

  if (method === "POST" && pathname === "/api/webdav-tokens") {
    return readBody(req).then((body) => {
      const requested = (body.username ?? "").trim();
      if (requested && !/^[A-Za-z0-9._-]{3,32}$/.test(requested)) {
        return json(res, { error: "Invalid username" }, 400);
      }
      const username =
        requested ||
        "sdw-" +
          Array.from(randomBytes(4))
            .map((b) => (b % 36).toString(36))
            .join("")
            .slice(0, 8);
      if (webDavTokens.some((t) => t.username === username)) {
        return json(res, { error: "Username already exists" }, 409);
      }
      const description = (body.description ?? "").trim() || undefined;
      const token = randomBytes(32).toString("base64url");
      const record = {
        id: randomUUID(),
        username,
        description,
        createdAt: new Date().toISOString(),
      };
      webDavTokens.unshift(record);
      return json(res, { ...record, token });
    });
  }

  {
    const m = pathname.match(/^\/api\/webdav-tokens\/(.+)$/);
    if (method === "DELETE" && m) {
      const before = webDavTokens.length;
      webDavTokens = webDavTokens.filter((t) => t.id !== m[1]);
      return empty(res, webDavTokens.length < before ? 204 : 404);
    }
  }

  // --- Existing media-library imports ---

  if (method === "GET" && pathname === "/api/media-library/sources") {
    return json(res, mediaLibrarySources);
  }

  if (method === "POST" && pathname === "/api/media-library/sources") {
    return readBody(req).then((body) => {
      const path = typeof body.path === "string" ? body.path.trim() : "";
      if (!path || !isAbsoluteServerPath(path)) {
        return json(res, { error: "An absolute server path is required" }, 400);
      }
      if (mediaLibrarySources.some((source) => source.path === path)) {
        return json(res, { error: "Import source already exists" }, 409);
      }

      const source = {
        id: randomUUID(),
        path,
        isMonitoring: body.isMonitoring === true,
        createdAt: new Date().toISOString(),
        lastScanAt: null,
        lastError: null,
        lastImportedCount: 0,
        lastUpdatedCount: 0,
        lastRemovedCount: 0,
        lastSkippedCount: 0,
        isScanning: false,
      };
      mediaLibrarySources.unshift(source);
      startMediaLibraryScan(source);
      return json(res, source, 201);
    });
  }

  {
    const scanMatch = pathname.match(
      /^\/api\/media-library\/sources\/([^/]+)\/scan$/,
    );
    if (method === "POST" && scanMatch) {
      const id = decodeURIComponent(scanMatch[1]);
      const source = mediaLibrarySources.find((item) => item.id === id);
      if (!source) return empty(res, 404);
      return json(res, { queued: startMediaLibraryScan(source) }, 202);
    }
  }

  {
    const sourceMatch = pathname.match(
      /^\/api\/media-library\/sources\/([^/]+)$/,
    );
    if (sourceMatch) {
      const id = decodeURIComponent(sourceMatch[1]);
      const source = mediaLibrarySources.find((item) => item.id === id);
      if (!source) return empty(res, 404);

      if (method === "PATCH") {
        return readBody(req).then((body) => {
          if (typeof body.isMonitoring !== "boolean") {
            return json(res, { error: "isMonitoring must be a boolean" }, 400);
          }
          source.isMonitoring = body.isMonitoring;
          return empty(res, 204);
        });
      }

      if (method === "DELETE") {
        if (source.isScanning) {
          return json(res, { error: "Source is being scanned" }, 409);
        }
        mediaLibrarySources = mediaLibrarySources.filter(
          (item) => item.id !== id,
        );
        return empty(res, 204);
      }
    }
  }

  // --- Files ---

  if (method === "GET" && pathname === "/api/file/list") {
    const id = searchParams.get("id");
    const relativeDir = searchParams.get("relativeDir") ?? "";
    const anim = animations.get(id);
    if (!anim || !anim.isDownloadFinished) return empty(res, 404);
    const listing = FILE_TREE[relativeDir];
    if (!listing) return empty(res, 404);
    return json(res, listing);
  }

  if (method === "POST" && pathname === "/api/file/generatelink") {
    return readBody(req).then((body) => {
      const resourceId = randomBytes(16).toString("base64url");
      res.setHeader(
        "Set-Cookie",
        `sdw-mock-playback=${randomBytes(32).toString("base64url")}; HttpOnly; SameSite=Strict; Path=/api/file/play`,
      );
      return json(res, { url: `/api/file/play/${resourceId}`, externalUrl: null });
    });
  }

  if (method === "GET" && pathname.startsWith("/api/file/play/")) {
    // Return a small placeholder response for mock playback
    res.writeHead(200, { "Content-Type": "text/plain" });
    return res.end(
      "Mock video playback — this would be a real video file in production.",
    );
  }

  // --- VFS (mirrors /api/vfs on the .NET backend) ---

  if (method === "GET" && pathname === "/api/vfs/stat") {
    const resolved = vfsResolve(searchParams.get("path"));
    if (!resolved) return empty(res, resolved === null ? 404 : 400);
    return json(res, resolved.entry);
  }

  if (method === "GET" && pathname === "/api/vfs/list") {
    const raw = searchParams.get("path") ?? "/";
    if (!raw.startsWith("/")) return empty(res, 400);
    const path = raw.length > 1 ? raw.replace(/\/+$/, "") : "/";
    const children = VFS_TREE[path];
    if (!children) {
      // 404 if missing, 400 if it's a known file path
      const resolved = vfsResolve(path);
      if (resolved && !resolved.isDirectory) return empty(res, 400);
      return empty(res, 404);
    }
    return json(res, children);
  }

  if (method === "GET" && pathname === "/api/vfs/read") {
    const resolved = vfsResolve(searchParams.get("path"));
    if (!resolved) return empty(res, 404);
    if (resolved.isDirectory) return empty(res, 404);
    res.writeHead(200, {
      "Content-Type": "application/octet-stream",
      "Content-Disposition": `attachment; filename="${encodeURIComponent(resolved.entry.name)}"`,
    });
    return res.end(`Mock VFS read — ${resolved.entry.name}`);
  }

  // --- Tasks ---

  const MOCK_TASKS = [
    {
      id: "SyncFeed",
      interval: "00:10:00",
      isEnabled: true,
      lastRunAt: new Date(Date.now() - 300_000).toISOString(),
      isRunning: false,
    },
    {
      id: "InferAnimationMetadata",
      interval: "00:30:00",
      isEnabled: true,
      lastRunAt: new Date(Date.now() - 600_000).toISOString(),
      isRunning: false,
    },
    {
      id: "ScrapeSeasonBangumi",
      interval: "7.00:00:00",
      isEnabled: true,
      lastRunAt: new Date(Date.now() - 86400_000).toISOString(),
      isRunning: false,
    },
    {
      id: "ScanMediaLibraries",
      interval: "00:05:00",
      isEnabled: true,
      lastRunAt: new Date(Date.now() - 120_000).toISOString(),
      isRunning: false,
    },
  ];

  if (method === "GET" && pathname === "/api/tasks") {
    return json(res, MOCK_TASKS);
  }

  // POST /api/tasks/:id/run
  {
    const m = pathname.match(/^\/api\/tasks\/(.+)\/run$/);
    if (method === "POST" && m) {
      const id = decodeURIComponent(m[1]);
      const task = MOCK_TASKS.find(
        (t) => t.id.toLowerCase() === id.toLowerCase(),
      );
      if (!task) return json(res, { message: `Task '${id}' not found` }, 404);
      task.lastRunAt = new Date().toISOString();
      console.log(`  Mock: task '${id}' executed`);
      return json(res, { message: `Task '${id}' completed` });
    }
  }

  // --- Chat ---
  const chatConversations =
    globalThis._chatConversations ?? (globalThis._chatConversations = []);
  const chatMessages =
    globalThis._chatMessages ?? (globalThis._chatMessages = new Map());

  // GET /api/chat/status
  if (method === "GET" && pathname === "/api/chat/status") {
    return json(res, {
      aiEnabled: isMockAiConfigured(),
      provider:
        systemSettings.ai.executionMode === "codexAppServer"
          ? "Codex App Server"
          : systemSettings.ai.provider === "openAI"
            ? "OpenAI"
            : "Anthropic",
    });
  }

  // GET /api/chat/models
  if (method === "GET" && pathname === "/api/chat/models") {
    if (!isMockAiConfigured())
      return json(res, { error: "AI is not configured" }, 503);
    if (systemSettings.ai.executionMode === "codexAppServer")
      return json(res, [
        {
          id: systemSettings.ai.codexAppServer.model || "app-server-default",
          name: systemSettings.ai.codexAppServer.model || "App-server default",
          provider: "Codex App Server",
        },
      ]);
    return json(res, [
      { id: "mock-gpt-4o", name: "Mock GPT-4o", provider: "MockAI" },
      { id: "mock-claude", name: "Mock Claude", provider: "MockAI" },
    ]);
  }

  // GET /api/chat/conversations
  if (method === "GET" && pathname === "/api/chat/conversations") {
    return json(
      res,
      chatConversations.sort(
        (a, b) => new Date(b.updatedAt) - new Date(a.updatedAt),
      ),
    );
  }

  // POST /api/chat/conversations
  if (method === "POST" && pathname === "/api/chat/conversations") {
    const body = await readBody(req);
    const conv = {
      id: randomUUID(),
      title: body.title || null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    chatConversations.push(conv);
    chatMessages.set(conv.id, []);
    return json(res, conv);
  }

  // GET /api/chat/conversations/:id
  {
    const m = pathname.match(/^\/api\/chat\/conversations\/([^/]+)$/);
    if (method === "GET" && m) {
      const conv = chatConversations.find((c) => c.id === m[1]);
      if (!conv) return empty(res, 404);
      return json(res, { ...conv, messages: chatMessages.get(conv.id) ?? [] });
    }
  }

  // DELETE /api/chat/conversations/:id
  {
    const m = pathname.match(/^\/api\/chat\/conversations\/([^/]+)$/);
    if (method === "DELETE" && m) {
      const idx = chatConversations.findIndex((c) => c.id === m[1]);
      if (idx === -1) return empty(res, 404);
      chatConversations.splice(idx, 1);
      chatMessages.delete(m[1]);
      return empty(res, 200);
    }
  }

  // PATCH /api/chat/conversations/:id
  {
    const m = pathname.match(/^\/api\/chat\/conversations\/([^/]+)$/);
    if (method === "PATCH" && m) {
      const conv = chatConversations.find((c) => c.id === m[1]);
      if (!conv) return empty(res, 404);
      const body = await readBody(req);
      if (body.title) conv.title = body.title;
      conv.updatedAt = new Date().toISOString();
      return empty(res, 200);
    }
  }

  // POST /api/chat/conversations/:id/messages — SSE mock
  {
    const m = pathname.match(/^\/api\/chat\/conversations\/([^/]+)\/messages$/);
    if (method === "POST" && m) {
      const convId = m[1];
      const conv = chatConversations.find((c) => c.id === convId);
      if (!conv) return empty(res, 404);

      const body = await readBody(req);
      const msgs = chatMessages.get(convId) ?? [];

      // Save user message
      msgs.push({
        id: randomUUID(),
        role: "user",
        content: body.content,
        toolCallsJson: null,
        toolCallId: null,
        toolName: null,
        order: msgs.length,
        createdAt: new Date().toISOString(),
      });

      // Auto-title
      if (!conv.title) {
        conv.title =
          body.content.length > 30
            ? body.content.slice(0, 30) + "..."
            : body.content;
      }
      conv.updatedAt = new Date().toISOString();

      // Set SSE headers
      res.writeHead(200, {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        Connection: "keep-alive",
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Headers": "Authorization, Content-Type",
      });

      // Build interleaved SSE steps: text -> tool call -> text -> finished
      const toolCallId = randomUUID();
      const preToolText = `让我帮你查一下相关信息。\n\n`;
      const postToolText =
        `## 查询结果\n\n` +
        `根据你的问题「${body.content}」，我找到了以下**相关信息**：\n\n` +
        `- 进击的巨人 最终季 完结篇\n` +
        `- 葬送的芙莉莲\n` +
        `- 药屋少女的呢喃\n\n` +
        `> 以上结果来自 TMDB 数据库查询。\n\n` +
        "```json\n" +
        `{ "total": 3, "source": "tmdb" }\n` +
        "```\n";

      const toolArgs = JSON.stringify({ query: body.content });
      const toolResult = JSON.stringify({
        results: [
          { id: 1, name: "进击的巨人 最终季 完结篇", tmdb_id: 94605 },
          { id: 2, name: "葬送的芙莉莲", tmdb_id: 209867 },
          { id: 3, name: "药屋少女的呢喃", tmdb_id: 225239 },
        ],
      });

      const steps = [];

      // Pre-tool text deltas (3 chars at a time)
      const preChars = [...preToolText];
      for (let i = 0; i < preChars.length; i += 3) {
        steps.push({
          event: "text_delta",
          data: { text: preChars.slice(i, i + 3).join("") },
          delay: 50,
        });
      }

      // Tool call begin
      steps.push({
        event: "tool_call_begin",
        data: { id: toolCallId, name: "search_tmdb" },
        delay: 100,
      });

      // Tool call argument deltas
      const argChars = [...toolArgs];
      for (let i = 0; i < argChars.length; i += 5) {
        steps.push({
          event: "tool_call_delta",
          data: {
            id: toolCallId,
            arguments_delta: argChars.slice(i, i + 5).join(""),
          },
          delay: 30,
        });
      }

      // Tool result (after a brief pause)
      steps.push({
        event: "tool_result",
        data: {
          tool_call_id: toolCallId,
          name: "search_tmdb",
          result: toolResult,
        },
        delay: 300,
      });

      // Post-tool text deltas
      const postChars = [...postToolText];
      for (let i = 0; i < postChars.length; i += 3) {
        steps.push({
          event: "text_delta",
          data: { text: postChars.slice(i, i + 3).join("") },
          delay: 30,
        });
      }

      // Stream steps with delays
      let stepIdx = 0;
      function sendNextStep() {
        if (stepIdx >= steps.length) {
          res.write(
            `event: finished\ndata: ${JSON.stringify({ stop_reason: "end_turn" })}\n\n`,
          );

          // Save assistant message with tool calls
          const fullText = preToolText + postToolText;
          msgs.push({
            id: randomUUID(),
            role: "assistant",
            content: fullText,
            toolCallsJson: JSON.stringify([
              { id: toolCallId, name: "search_tmdb", arguments: toolArgs },
            ]),
            toolCallId: null,
            toolName: null,
            order: msgs.length,
            createdAt: new Date().toISOString(),
          });
          // Save tool result message
          msgs.push({
            id: randomUUID(),
            role: "tool",
            content: toolResult,
            toolCallsJson: null,
            toolCallId: toolCallId,
            toolName: "search_tmdb",
            order: msgs.length,
            createdAt: new Date().toISOString(),
          });

          res.end();
          return;
        }

        const step = steps[stepIdx++];
        res.write(
          `event: ${step.event}\ndata: ${JSON.stringify(step.data)}\n\n`,
        );
        setTimeout(sendNextStep, step.delay);
      }

      sendNextStep();
      return;
    }
  }

  // --- 404 ---
  return empty(res, 404);
}

// ---------------------------------------------------------------------------
// Server
// ---------------------------------------------------------------------------

const server = createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  const result = route(
    req.method,
    url.pathname.toLowerCase(),
    url.searchParams,
    req,
    res,
  );
  // Handle async routes (readBody returns a Promise)
  if (result instanceof Promise) {
    result.catch((err) => {
      console.error("Error handling request:", err);
      empty(res, 500);
    });
  }
});
server.listen(PORT, () => {
  console.log(`Mock API server running on http://localhost:${PORT}`);
  const finishedCount = [...animations.values()].filter(
    (animation) => animation.isDownloadFinished,
  ).length;
  const downloadingCount = [...animations.values()].filter(
    (animation) => animation.isDownloadTracked && !animation.isDownloadFinished,
  ).length;
  console.log(
    `  ${animations.size} anime entries (${finishedCount} finished, ${downloadingCount} active downloads, rest untracked)`,
  );
  console.log(`  ${feeds.length} RSS feeds`);
  console.log(`  Auth: any password works (register first on first visit)`);
  console.log(
    `\nRun "yarn start" in another terminal to start the frontend dev server.`,
  );
});
