// Mock API server for frontend development/testing.
// Run with: yarn mock (or: node mock-server.mjs)
// Then run: yarn start — the Parcel proxy forwards /api/* to this server.

import { createServer } from "node:http";
import { randomUUID, randomBytes } from "node:crypto";

const PORT = parseInt(process.env.MOCK_PORT ?? "5097", 10);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function json(res, data, status = 200) {
  res.writeHead(status, {
    "Content-Type": "application/json",
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
    "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
  });
  res.end(JSON.stringify(data));
}

function empty(res, status = 200) {
  res.writeHead(status, {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Authorization, Content-Type",
    "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
  });
  res.end();
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

const ANIME_TITLES = [
  { title: "[Mikanani] 葬送的芙莉莲 / Sousou no Frieren - 28 (1080p)", desc: "勇者一行的魔法使芙莉莲的旅途故事", season: 1, episode: 28 },
  { title: "[SubsPlease] 迷宫饭 / Dungeon Meshi - 24 (1080p)", desc: "在地下城中烹饪魔物的冒险者们", season: 1, episode: 24 },
  { title: "[Mikanani] 药屋少女的呢喃 / Kusuriya no Hitorigoto - 24 (1080p)", desc: "后宫药师猫猫的推理日常", season: 1, episode: 24 },
  { title: "[ANi] 我心里危险的东西 第二季 - 13 (1080p)", desc: "市川与山田的青春恋爱物语", season: 2, episode: 13 },
  { title: "[Mikanani] 物语系列 Off & Monster Season - 12 (1080p)", desc: "阿良良木历的怪异故事继续", season: 1, episode: 12 },
  { title: "[SubsPlease] 鬼灭之刃 柱训练篇 - 08 (1080p)", desc: "炭治郎与柱们的训练篇章", season: 4, episode: 8 },
  { title: "[Mikanani] 无职转生 第三季 - 12 (1080p)", desc: "鲁迪乌斯的异世界冒险续篇", season: 3, episode: 12 },
  { title: "[ANi] 败犬女主太多了 - 12 (1080p)", desc: "温水和是被选中的那个男人", season: 1, episode: 12 },
  { title: "[SubsPlease] 夏日重现 / Summer Time Rendering - 25 (1080p)", desc: "小岛上的时间循环悬疑故事", season: 1, episode: 25 },
  { title: "[Mikanani] 孤独摇滚 / Bocchi the Rock! - 12 (1080p)", desc: "社恐少女后藤一里的乐队之路", season: 1, episode: 12 },
  { title: "[ANi] 间谍过家家 第三季 - 12 (1080p)", desc: "黄昏一家的间谍喜剧日常", season: 3, episode: 12 },
  { title: "[SubsPlease] 青之箱 / Blue Box - 24 (1080p)", desc: "大喜与千夏的恋爱与羽毛球", season: 1, episode: 24 },
  { title: "[Mikanani] Re:从零开始的异世界生活 第三季 - 16 (1080p)", desc: "昴在异世界的又一次轮回", season: 3, episode: 16 },
  { title: "[ANi] 魔法少女毁灭者 - 12 (1080p)", desc: "以暴力手段对抗魔法少女", season: 1, episode: 12 },
  { title: "[SubsPlease] 怪兽8号 / Kaiju No. 8 - 12 (1080p)", desc: "日比野卡夫卡的怪兽之力", season: 1, episode: 12 },
  { title: "[Mikanani] 天国大魔境 / Tengoku Daimakyou - 13 (1080p)", desc: "废墟日本的末日生存之旅", season: 1, episode: 13 },
  { title: "[ANi] 摇曳露营 第三季 - 12 (1080p)", desc: "志�的与凛的户外露营日常", season: 3, episode: 12 },
  { title: "[SubsPlease] 排球少年 垃圾场决战 (1080p)", desc: "音驹 vs 乌野的巅峰之战", season: 4, episode: null },
  { title: "[Mikanani] 地错 第五季 - 12 (1080p)", desc: "贝尔在地下城寻求邂逅", season: 5, episode: 12 },
  { title: "[ANi] 樱子小姐的脚下埋着尸体 - 12 (1080p)", desc: "九条樱子的骸骨推理", season: 1, episode: 12 },
  { title: "[SubsPlease] 异世界自杀小队 - 10 (1080p)", desc: "DC反派们的异世界冒险", season: 1, episode: 10 },
  { title: "[Mikanani] 忧国的莫里亚蒂 - 24 (1080p)", desc: "犯罪咨询师莫里亚蒂的故事", season: 1, episode: 24 },
  { title: "[ANi] 为美好的世界献上爆焰 - 12 (1080p)", desc: "惠惠的爆裂魔法前传", season: 1, episode: 12 },
  { title: "[SubsPlease] 总之就是非常可爱 第三季 - 12 (1080p)", desc: "由崎星空与司的甜蜜日常", season: 3, episode: 12 },
  { title: "[Mikanani] 别当欧尼酱了 第二季 - 12 (1080p)", desc: "绪山真寻的女子高中生活", season: 2, episode: 12 },
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

    if (i < 3) {
      // First 3: finished
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
      season: entry.season,
      episode: entry.episode,
      group: { name: entry.title.match(/\[(.+?)\]/)?.[1] ?? "Fansub" },
      animation: {
        name: entry.title.split("] ")[1]?.split(" - ")[0] ?? entry.title,
        englishName: "",
        originalName: "",
      },
    });
  });
}

initAnimations();

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
        }
      }
    }
  }
}, 1000);

// Mock season bangumi data
const SEASON_BANGUMIS = [
  { mikanId: 3899, title: "尖帽子的魔法工房", dayOfWeek: 1, imageUrl: null },
  { mikanId: 3904, title: "自称恶役大小姐的婚约者观察记录。", dayOfWeek: 1, imageUrl: null },
  { mikanId: 3850, title: "吹响吧！上低音号 第三季", dayOfWeek: 2, imageUrl: null },
  { mikanId: 3880, title: "怪异与少女与神隐", dayOfWeek: 2, imageUrl: null },
  { mikanId: 3870, title: "暗杀贵族 第二季", dayOfWeek: 3, imageUrl: null },
  { mikanId: 3815, title: "转生贵族的异世界冒险录 第二季", dayOfWeek: 3, imageUrl: null },
  { mikanId: 3910, title: "无名记忆 第二季", dayOfWeek: 4, imageUrl: null },
  { mikanId: 3920, title: "我的幸福婚约 第三季", dayOfWeek: 4, imageUrl: null },
  { mikanId: 3860, title: "关于我转生变成史莱姆这档事 第四季", dayOfWeek: 5, imageUrl: null },
  { mikanId: 3890, title: "迷宫饭 第二季", dayOfWeek: 5, imageUrl: null },
  { mikanId: 3841, title: "鬼灭之刃 无限城篇", dayOfWeek: 6, imageUrl: null },
  { mikanId: 3900, title: "恋上换装娃娃 第三季", dayOfWeek: 6, imageUrl: null },
  { mikanId: 227, title: "名侦探柯南", dayOfWeek: 0, imageUrl: null },
  { mikanId: 228, title: "航海王", dayOfWeek: 0, imageUrl: null },
  { mikanId: 3950, title: "剧场版 紫罗兰永恒花园", dayOfWeek: 7, imageUrl: null },
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
  { id: randomUUID(), url: "https://mikanani.me/RSS/Bangumi?bangumiId=3141", name: "葬送的芙莉莲", createdAt: new Date(Date.now() - 86400_000 * 3).toISOString() },
  { id: randomUUID(), url: "https://mikanani.me/RSS/Bangumi?bangumiId=3143", name: "迷宫饭", createdAt: new Date(Date.now() - 86400_000 * 2).toISOString() },
  { id: randomUUID(), url: "https://mikanani.me/RSS/Bangumi?bangumiId=3200", name: "药屋少女的呢喃", createdAt: new Date(Date.now() - 86400_000).toISOString() },
];

// Mock file tree
const FILE_TREE = {
  "": [
    { fileName: "Season 1", isDirectory: true, relative: "Season 1" },
    { fileName: "Season 2", isDirectory: true, relative: "Season 2" },
    { fileName: "Specials", isDirectory: true, relative: "Specials" },
  ],
  "Season 1": Array.from({ length: 12 }, (_, i) => ({
    fileName: `EP${String(i + 1).padStart(2, "0")}.mp4`,
    isDirectory: false,
    relative: null,
  })),
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

// ---------------------------------------------------------------------------
// Router
// ---------------------------------------------------------------------------

function route(method, pathname, searchParams, req, res) {
  console.log(`${method} ${pathname}${searchParams.toString() ? "?" + searchParams : ""}`);

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
    return json(res, { token: fakeToken(), refreshToken: fakeToken(), success: true });
  }

  if (method === "POST" && pathname === "/api/auth/login") {
    if (!registered) return json(res, { token: "", refreshToken: "", success: false });
    return json(res, { token: fakeToken(), refreshToken: fakeToken(), success: true });
  }

  if (method === "POST" && pathname === "/api/auth/refresh") {
    return json(res, { token: fakeToken(), refreshToken: fakeToken(), success: true });
  }

  if (method === "GET" && pathname === "/api/auth/verify") {
    if (!hasAuth(req)) return empty(res, 401);
    return json(res, [{ Type: "sub", Value: "mock-user" }]);
  }

  // --- All remaining endpoints require auth ---
  if (!hasAuth(req) && !pathname.startsWith("/api/auth/")) {
    return empty(res, 401);
  }

  // --- Animation Info ---

  if (method === "GET" && pathname === "/api/animationinfo") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const all = [...animations.values()];
    return json(res, { data: all.slice(skip, skip + take), totalItems: all.length });
  }

  if (method === "GET" && pathname === "/api/animationinfo/downloading") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const list = [...animations.values()].filter(
      (a) => a.isDownloadTracked && !a.isDownloadFinished,
    );
    return json(res, { data: list.slice(skip, skip + take), totalItems: list.length });
  }

  if (method === "GET" && pathname === "/api/animationinfo/downloaded") {
    const skip = parseInt(searchParams.get("skip") ?? "0", 10);
    const take = parseInt(searchParams.get("take") ?? "10", 10);
    const list = [...animations.values()].filter((a) => a.isDownloadFinished);
    return json(res, { data: list.slice(skip, skip + take), totalItems: list.length });
  }

  // GET /api/animationinfo/status/:id
  {
    const m = pathname.match(/^\/api\/animationinfo\/status\/(.+)$/);
    if (method === "GET" && m) {
      const id = m[1];
      const ds = downloadState.get(id);
      if (!ds) return empty(res, 404);
      const speed = ds.state === "Downloading" ? 2_500_000 + Math.random() * 5_000_000 : 0;
      const remaining = ds.state === "Downloading" && ds.progress > 0
        ? ((1 - ds.progress) / 0.02) // ~seconds left
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
      downloadState.set(id, { state: "Downloading", progress: 0, startedAt: Date.now() });
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
      downloadState.delete(id);
      return empty(res, 200);
    }
  }

  // --- Season Bangumi ---

  if (method === "GET" && pathname === "/api/season") {
    const year = searchParams.get("year");
    const season = searchParams.get("season");
    const seasonLabels = { "春": "春季", "夏": "夏季", "秋": "秋季", "冬": "冬季" };

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

    const lastScrapedAt = SEASON_BANGUMIS.length > 0 ? SEASON_BANGUMIS[0].scrapedAt : null;
    return json(res, { year: null, season: null, lastScrapedAt, bangumis: SEASON_BANGUMIS });
  }

  if (method === "POST" && pathname === "/api/season/refresh") {
    const lastScrapedAt = SEASON_BANGUMIS.length > 0 ? SEASON_BANGUMIS[0].scrapedAt : null;
    return json(res, { lastScrapedAt, bangumis: SEASON_BANGUMIS });
  }

  // GET /api/season/:mikanId/subgroups
  {
    const m = pathname.match(/^\/api\/season\/(\d+)\/subgroups$/);
    if (method === "GET" && m) {
      const mikanId = parseInt(m[1], 10);
      const subgroups = (MOCK_SUBGROUPS[mikanId] ?? [
        { mikanSubgroupId: 370, name: "LoliHouse" },
        { mikanSubgroupId: 202, name: "ANi" },
      ]).map((sg) => ({
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
      return empty(res, feeds.length < before ? 200 : 404);
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
      const token = randomBytes(32).toString("base64url");
      return json(res, { url: `/api/file/play?token=${token}` });
    });
  }

  if (method === "GET" && pathname === "/api/file/play") {
    // Return a small placeholder response for mock playback
    res.writeHead(200, { "Content-Type": "text/plain" });
    return res.end("Mock video playback — this would be a real video file in production.");
  }

  // --- Tasks ---

  const MOCK_TASKS = [
    { name: "SyncFeed", description: "同步 RSS 订阅", interval: "00:10:00", isEnabled: true, lastRunAt: new Date(Date.now() - 300_000).toISOString(), isRunning: false },
    { name: "InferAnimationMetadata", description: "AI 元数据推断", interval: "00:30:00", isEnabled: true, lastRunAt: new Date(Date.now() - 600_000).toISOString(), isRunning: false },
    { name: "ScrapeSeasonBangumi", description: "更新当季番组列表", interval: "7.00:00:00", isEnabled: true, lastRunAt: new Date(Date.now() - 86400_000).toISOString(), isRunning: false },
  ];

  if (method === "GET" && pathname === "/api/tasks") {
    return json(res, MOCK_TASKS);
  }

  // POST /api/tasks/:name/run
  {
    const m = pathname.match(/^\/api\/tasks\/(.+)\/run$/);
    if (method === "POST" && m) {
      const name = decodeURIComponent(m[1]);
      const task = MOCK_TASKS.find((t) => t.name.toLowerCase() === name.toLowerCase());
      if (!task) return json(res, { message: `Task '${name}' not found` }, 404);
      task.lastRunAt = new Date().toISOString();
      console.log(`  Mock: task '${name}' executed`);
      return json(res, { message: `Task '${name}' completed` });
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
  const result = route(req.method, url.pathname.toLowerCase(), url.searchParams, req, res);
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
  console.log(`  ${animations.size} anime entries (3 finished, 2 downloading, 1 paused, rest untracked)`);
  console.log(`  ${feeds.length} RSS feeds`);
  console.log(`  Auth: any password works (register first on first visit)`);
  console.log(`\nRun "yarn start" in another terminal to start the frontend dev server.`);
});
