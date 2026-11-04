using System.Globalization;
using System.Net;
using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public sealed partial class SonarClient
{
    private string? _chatMixPath;

    public async Task<ChatMixState> GetChatMixAsync(CancellationToken cancellationToken = default)
    {
        if (_chatMixPath is not null)
        {
            return ValidateChatMix(await GetAsync<ChatMixState>(_chatMixPath, cancellationToken).ConfigureAwait(false));
        }

        try
        {
            var state = ValidateChatMix(await GetAsync<ChatMixState>("/v1/chatMix", cancellationToken).ConfigureAwait(false));
            _chatMixPath = "/v1/chatMix";
            return state;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            var state = ValidateChatMix(await GetAsync<ChatMixState>("/chatMix", cancellationToken).ConfigureAwait(false));
            _chatMixPath = "/chatMix";
            return state;
        }
    }

    public async Task<ChatMixState> SetChatMixAsync(float balance, CancellationToken cancellationToken = default)
    {
        if (!float.IsFinite(balance) || balance is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(balance));
        }

        var current = await GetChatMixAsync(cancellationToken).ConfigureAwait(false);
        if (!current.CanAdjust)
        {
            throw new InvalidOperationException("Sonar 当前不允许调整聊天混音。");
        }

        await PutAsync($"{_chatMixPath}?balance={balance.ToString("R", CultureInfo.InvariantCulture)}", cancellationToken)
            .ConfigureAwait(false);
        var confirmed = await GetChatMixAsync(cancellationToken).ConfigureAwait(false);
        if (Math.Abs(confirmed.Balance - balance) > 0.015f)
        {
            throw new InvalidDataException("Sonar 未确认聊天混音变化，请重试。");
        }

        return confirmed;
    }

    private static ChatMixState ValidateChatMix(ChatMixState state)
    {
        if (!float.IsFinite(state.Balance) || state.Balance is < -1 or > 1 || string.IsNullOrWhiteSpace(state.State))
        {
            throw new InvalidDataException("Sonar 返回了无效的聊天混音状态。");
        }

        return state;
    }
}
