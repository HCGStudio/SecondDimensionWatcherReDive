// In-memory counterparts of the independent profile settings endpoints.
const avatars = new Map();

export async function handleProfileSettings(
  req,
  res,
  pathname,
  profiles,
  json,
  empty,
) {
  const match = pathname.match(
    /^\/api\/accounts\/profiles\/([0-9a-f-]+)\/(name|pin|avatar)$/i,
  );
  if (!match) return false;
  const profile = profiles.find((item) => item.id === match[1]);
  if (!profile) {
    empty(res, 404);
    return true;
  }
  const setting = match[2];
  if (setting === "avatar" && req.method === "GET") {
    const avatar = avatars.get(profile.id);
    if (!avatar) {
      empty(res, 404);
      return true;
    }
    res.writeHead(200, {
      "Content-Type": avatar.type,
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    });
    res.end(avatar.data);
    return true;
  }
  if (setting === "avatar" && req.method === "DELETE") {
    avatars.delete(profile.id);
    profile.avatar = null;
    empty(res, 204);
    return true;
  }
  if (req.method !== "PUT") {
    empty(res, 405);
    return true;
  }
  const chunks = [];
  let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > 2 * 1024 * 1024 + 65536) {
      empty(res, 413);
      return true;
    }
    chunks.push(chunk);
  }
  const data = Buffer.concat(chunks);
  try {
    if (setting === "avatar") {
      const request = new Request("http://localhost/avatar", {
        method: "PUT",
        headers: req.headers,
        body: data,
      });
      const form = await request.formData();
      const file = form.get("file");
      if (
        !file ||
        typeof file === "string" ||
        file.size === 0 ||
        file.size > 2 * 1024 * 1024 ||
        !["image/png", "image/jpeg"].includes(file.type)
      ) {
        empty(res, 400);
        return true;
      }
      avatars.set(profile.id, {
        type: file.type,
        data: Buffer.from(await file.arrayBuffer()),
      });
      profile.avatar = `/api/accounts/profiles/${profile.id}/avatar?v=${Date.now()}`;
    } else {
      const body = JSON.parse(data.toString());
      if (setting === "name") {
        const name = body.name?.trim();
        if (!name || name.length > 64) {
          empty(res, 400);
          return true;
        }
        if (
          profiles.some((item) => item.id !== profile.id && item.name === name)
        ) {
          empty(res, 409);
          return true;
        }
        profile.name = name;
      } else {
        if (body.pin && !/^[0-9]{4,8}$/.test(body.pin)) {
          empty(res, 400);
          return true;
        }
        profile.hasPin = Boolean(body.pin);
      }
    }
    empty(res, 204);
  } catch {
    json(res, { message: "Invalid profile settings request." }, 400);
  }
  return true;
}
