using System.Text;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Services;

const int Depth = 10_000;
var prefix = Encoding.ASCII.GetBytes("d4:info");
var nested = Encoding.ASCII.GetBytes(
    new string('l', Depth) + "0:" + new string('e', Depth));
var payload = new byte[prefix.Length + nested.Length + 1];
prefix.CopyTo(payload, 0);
nested.CopyTo(payload, prefix.Length);
payload[^1] = (byte)'e';

try
{
    _ = SyncFeed.ParseTorrentData(payload, "internal://bencode-survival-probe");
    return 1;
}
catch (InvalidTorrentDataException)
{
    return 0;
}
