using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.Animation;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive;

[JsonSerializable(typeof(ResponseData<IEnumerable<AnimationInfoDto>>))]
[JsonSerializable(typeof(ResponseData<List<AnimationInfoDto>>))]
[JsonSerializable(typeof(AnimationGroupedResponse))]
[JsonSerializable(typeof(FileDownloadStatus))]
[JsonSerializable(typeof(List<Feed>))]
[JsonSerializable(typeof(Feed))]
[JsonSerializable(typeof(List<TasksController.TaskDto>))]
[JsonSerializable(typeof(SeasonController.SeasonResponse))]
[JsonSerializable(typeof(List<SeasonController.SubgroupDto>))]
[JsonSerializable(typeof(SeasonController.SubscribeRequest))]
[JsonSerializable(typeof(AuthController.LoginData))]
[JsonSerializable(typeof(AuthController.LoginResult))]
[JsonSerializable(typeof(AuthController.AuthRequest))]
[JsonSerializable(typeof(FileController.FileLinkResultResponse))]
[JsonSerializable(typeof(FileController.FileLinkResultRequest))]
[JsonSerializable(typeof(IEnumerable<FileController.FileStoreListResult>))]
[JsonSerializable(typeof(FileController.FileStoreListResult[]))]
[JsonSerializable(typeof(FileController.FileStoreToken))]
[JsonSerializable(typeof(AuthController.RefreshToken))]
[JsonSerializable(typeof(FeedController.AddFeedRequest))]
[JsonSerializable(typeof(PasswordConfig))]
public partial class AppJsonSerializerContext : JsonSerializerContext;
