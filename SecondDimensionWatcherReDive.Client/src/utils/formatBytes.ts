export const formatBytes = (bytes: number) => {
  if (!+bytes) return "0 B/s";

  const k = 1024;
  const sizes = [
    "B/s",
    "KB/s",
    "MB/s",
    "GB/s",
    "TB/s",
    "PB/s",
    "EB/s",
    "ZB/s",
    "YB/s",
  ];

  const i = Math.floor(Math.log(bytes) / Math.log(k));

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
};
