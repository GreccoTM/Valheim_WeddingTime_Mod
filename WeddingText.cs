using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;

namespace WeddingTime
{
    // Player-facing text. Messages that cross the network carry a key plus arguments, never final text,
    // so each client shows them in its own language. An argument can itself be a key (KeyArg) or a
    // duration (DurationArg); both are resolved by the receiving client.
    internal static class WeddingText
    {
        private const string TokenPrefix = "$weddingtime_";
        private const string DurationPrefix = "#duration:";

        internal static void Register()
        {
            var localization = LocalizationManager.Instance.GetLocalization();
            localization.AddTranslation("English", English);
            localization.AddTranslation("Portuguese_Brazilian", PortugueseBrazilian);
        }

        internal static string T(string key, params string[] args)
        {
            var resolved = (args ?? new string[0]).Select(Resolve).ToArray();
            return Localization.instance.Localize(TokenPrefix + key, resolved);
        }

        // An argument that the receiving client translates itself.
        internal static string KeyArg(string key) => TokenPrefix + key;
        internal static string DurationArg(int seconds) => DurationPrefix + seconds;

        internal static string Duration(int seconds)
        {
            if (seconds >= 120) return T("duration_minutes", (seconds / 60).ToString());
            if (seconds >= 60) return T("duration_minute");
            return T("duration_seconds", seconds.ToString());
        }

        internal static void Show(string text)
        {
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            if (Chat.instance != null) Chat.instance.AddString(text);
        }

        private static string Resolve(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return arg ?? string.Empty;
            if (arg.StartsWith(TokenPrefix)) return Localization.instance.Localize(arg);
            if (arg.StartsWith(DurationPrefix) && int.TryParse(arg.Substring(DurationPrefix.Length), out var seconds))
            {
                return Duration(seconds);
            }
            return arg;
        }

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "weddingtime_ring_name", "Wedding Ring" },
            { "weddingtime_ring_description", "Crafted for someone special. After accepting a proposal, equip the ring made by your partner to share the Wedding Bond." },
            { "weddingtime_bond_name", "Wedding Bond" },
            { "weddingtime_bond_level", "Lv. $1/10" },
            { "weddingtime_effect_doubled", " (doubled by the upgraded ring)" },
            { "weddingtime_effect_1", "Lv1: +$1% stamina regeneration" },
            { "weddingtime_effect_2", "Lv2: +$1% movement speed" },
            { "weddingtime_effect_3", "Lv3: +$1 carry weight" },
            { "weddingtime_effect_4", "Lv4: +$1% health regeneration" },
            { "weddingtime_effect_5", "Lv5: $1% less fire, frost, lightning, poison, and spirit damage" },
            { "weddingtime_effect_6", "Lv6: +$1% Eitr regeneration" },
            { "weddingtime_effect_7", "Lv7: $1% less stamina cost" },
            { "weddingtime_effect_8", "Lv8: +$1 to all skills" },
            { "weddingtime_effect_9", "Lv9: +$1 maximum health, stamina, and Eitr" },
            { "weddingtime_effect_10", "Lv10: $1% less stagger" },

            { "weddingtime_not_ready", "The world is not ready yet." },
            { "weddingtime_not_connected", "Not connected to the server." },
            { "weddingtime_already_engaged", "You are already engaged or married. Divorce first before proposing again." },
            { "weddingtime_already_engaged_other", "You are already engaged to another player." },
            { "weddingtime_proposal_pending", "You already have a proposal awaiting an answer." },
            { "weddingtime_no_player_named", "No nearby player named '$1' was found. Player names must match exactly." },
            { "weddingtime_multiple_players_named", "More than one nearby player is named '$1'. Ask them to use distinct character names." },
            { "weddingtime_get_closer", "Get within $1 meters of $2 before proposing." },
            { "weddingtime_proposal_sent", "You asked $1, ‘Will you marry me?’ They have two minutes to answer with /i_do or /i_do_not." },
            { "weddingtime_proposal_received", "$1 asks: ‘Will you marry me?’ Answer with /i_do or /i_do_not within two minutes." },
            { "weddingtime_no_pending_proposal", "You do not have a pending marriage proposal." },
            { "weddingtime_proposer_gone", "The proposer is no longer nearby. The proposal expired." },
            { "weddingtime_other_proposal_waiting", "You already have another proposal waiting for an answer." },
            { "weddingtime_accepted_self", "You said ‘I do’ to $1! You are engaged. Each of you can now craft a Wedding Ring for the other." },
            { "weddingtime_accepted_other", "$1 said ‘I do’! You are engaged. Each of you can now craft a Wedding Ring for the other." },
            { "weddingtime_declined_self", "You declined $1's proposal." },
            { "weddingtime_reply", "$1 replied: ‘$2’" },
            { "weddingtime_reply_no", "I do not." },
            { "weddingtime_reply_busy", "I cannot accept another proposal right now." },
            { "weddingtime_reply_not_nearby", "The proposer is no longer nearby." },
            { "weddingtime_reply_other_waiting", "I already have another proposal waiting for an answer." },
            { "weddingtime_marriage_failed", "The marriage could not be completed: $1" },
            { "weddingtime_reason_already_married", "one of you is already married." },
            { "weddingtime_reason_timeout", "your partner did not confirm in time." },
            { "weddingtime_reason_not_confirmed", "the server never confirmed it." },
            { "weddingtime_marriage_recorded", "Your marriage to $1 is now recorded in this world." },
            { "weddingtime_marriage_missing", "Your marriage is no longer recorded in this world." },
            { "weddingtime_ended_while_away", "Your marriage to $1 ended while you were away." },
            { "weddingtime_your_partner", "Your partner" },

            { "weddingtime_progress_unavailable", "Wedding Bond progress is unavailable right now. Enter a world first." },
            { "weddingtime_progress_not_married", "You are not married in this world. Wedding Bond progress has not started." },
            { "weddingtime_progress_max", "Wedding Bond with $1: Level 10/10 — maximum level reached ($2 shared days)." },
            { "weddingtime_progress", "Wedding Bond with $1: Level $2/10 — $3/$4 shared days; $5 days until Level $6." },
            { "weddingtime_celebration", "Congratulations to $1 and $2! Their Wedding Bond reached Level $3!" },

            { "weddingtime_not_married", "You are not married." },
            { "weddingtime_divorce_warning", "Divorcing $1 ends your Wedding Bond and erases your shared progress.$2 Type /divorce confirm within $3 seconds to proceed." },
            { "weddingtime_divorce_warning_wait", " The divorce becomes final $1 after you confirm; either of you can cancel before then." },
            { "weddingtime_nothing_to_confirm", "Nothing to confirm. Type /divorce first." },
            { "weddingtime_request_sent", "Request sent." },
            { "weddingtime_divorce_requested", "Divorce requested. It becomes final $1 from now. Use /divorce cancel to stop it." },
            { "weddingtime_divorce_requested_partner", "$1 requested a divorce. It becomes final $2 from now. Use /divorce cancel to stop it." },
            { "weddingtime_divorce_already_pending", "A divorce is already pending. Use /divorce cancel to stop it." },
            { "weddingtime_divorce_cancelled", "The divorce was cancelled." },
            { "weddingtime_no_pending_divorce", "There is no pending divorce." },
            { "weddingtime_marriage_ended", "Your marriage has ended." },
            { "weddingtime_admin_only", "Only server admins can force a divorce." },
            { "weddingtime_admin_ended", "A server admin ended this marriage." },
            { "weddingtime_admin_done", "Ended the marriage of $1." },
            { "weddingtime_target_not_found", "No online player named '$1' was found. Use their player ID if they are offline." },
            { "weddingtime_target_ambiguous", "More than one online player is named '$1'. Use their player ID instead." },
            { "weddingtime_target_not_married", "That player is not married." },

            { "weddingtime_duration_minutes", "in $1 minutes" },
            { "weddingtime_duration_minute", "in 1 minute" },
            { "weddingtime_duration_seconds", "in $1 seconds" },
        };

        private static readonly Dictionary<string, string> PortugueseBrazilian = new Dictionary<string, string>
        {
            { "weddingtime_ring_name", "Aliança de Casamento" },
            { "weddingtime_ring_description", "Feita para alguém especial. Depois de aceitar um pedido, equipe a aliança feita pelo seu parceiro para compartilhar o Laço do Casamento." },
            { "weddingtime_bond_name", "Laço do Casamento" },
            { "weddingtime_bond_level", "Nv. $1/10" },
            { "weddingtime_effect_doubled", " (dobrado pela aliança aprimorada)" },
            { "weddingtime_effect_1", "Nv1: +$1% de regeneração de vigor" },
            { "weddingtime_effect_2", "Nv2: +$1% de velocidade de movimento" },
            { "weddingtime_effect_3", "Nv3: +$1 de capacidade de carga" },
            { "weddingtime_effect_4", "Nv4: +$1% de regeneração de vida" },
            { "weddingtime_effect_5", "Nv5: $1% menos dano de fogo, gelo, raio, veneno e espírito" },
            { "weddingtime_effect_6", "Nv6: +$1% de regeneração de Eitr" },
            { "weddingtime_effect_7", "Nv7: $1% menos custo de vigor" },
            { "weddingtime_effect_8", "Nv8: +$1 em todas as habilidades" },
            { "weddingtime_effect_9", "Nv9: +$1 de vida, vigor e Eitr máximos" },
            { "weddingtime_effect_10", "Nv10: $1% menos atordoamento" },

            { "weddingtime_not_ready", "O mundo ainda não está pronto." },
            { "weddingtime_not_connected", "Sem conexão com o servidor." },
            { "weddingtime_already_engaged", "Você já está noivo(a) ou casado(a). Divorcie-se antes de fazer outro pedido." },
            { "weddingtime_already_engaged_other", "Você já está noivo(a) de outro jogador." },
            { "weddingtime_proposal_pending", "Você já tem um pedido aguardando resposta." },
            { "weddingtime_no_player_named", "Nenhum jogador próximo chamado '$1' foi encontrado. O nome precisa ser exato." },
            { "weddingtime_multiple_players_named", "Há mais de um jogador próximo chamado '$1'. Peça que usem nomes de personagem diferentes." },
            { "weddingtime_get_closer", "Chegue a até $1 metros de $2 antes de fazer o pedido." },
            { "weddingtime_proposal_sent", "Você perguntou a $1: ‘Quer casar comigo?’ A pessoa tem dois minutos para responder com /i_do ou /i_do_not." },
            { "weddingtime_proposal_received", "$1 pergunta: ‘Quer casar comigo?’ Responda com /i_do ou /i_do_not em até dois minutos." },
            { "weddingtime_no_pending_proposal", "Você não tem nenhum pedido de casamento pendente." },
            { "weddingtime_proposer_gone", "Quem fez o pedido não está mais por perto. O pedido expirou." },
            { "weddingtime_other_proposal_waiting", "Você já tem outro pedido aguardando resposta." },
            { "weddingtime_accepted_self", "Você disse ‘Sim’ para $1! Vocês estão noivos. Agora cada um pode fabricar uma Aliança de Casamento para o outro." },
            { "weddingtime_accepted_other", "$1 disse ‘Sim’! Vocês estão noivos. Agora cada um pode fabricar uma Aliança de Casamento para o outro." },
            { "weddingtime_declined_self", "Você recusou o pedido de $1." },
            { "weddingtime_reply", "$1 respondeu: ‘$2’" },
            { "weddingtime_reply_no", "Não aceito." },
            { "weddingtime_reply_busy", "Não posso aceitar outro pedido agora." },
            { "weddingtime_reply_not_nearby", "Quem fez o pedido não está mais por perto." },
            { "weddingtime_reply_other_waiting", "Já tenho outro pedido aguardando resposta." },
            { "weddingtime_marriage_failed", "O casamento não pôde ser concluído: $1" },
            { "weddingtime_reason_already_married", "um de vocês já é casado." },
            { "weddingtime_reason_timeout", "seu parceiro não confirmou a tempo." },
            { "weddingtime_reason_not_confirmed", "o servidor nunca o confirmou." },
            { "weddingtime_marriage_recorded", "Seu casamento com $1 agora está registrado neste mundo." },
            { "weddingtime_marriage_missing", "Seu casamento não está mais registrado neste mundo." },
            { "weddingtime_ended_while_away", "Seu casamento com $1 terminou enquanto você estava fora." },
            { "weddingtime_your_partner", "Seu parceiro" },

            { "weddingtime_progress_unavailable", "O progresso do Laço do Casamento não está disponível agora. Entre em um mundo primeiro." },
            { "weddingtime_progress_not_married", "Você não é casado(a) neste mundo. O progresso do Laço do Casamento ainda não começou." },
            { "weddingtime_progress_max", "Laço do Casamento com $1: Nível 10/10 — nível máximo alcançado ($2 dias juntos)." },
            { "weddingtime_progress", "Laço do Casamento com $1: Nível $2/10 — $3/$4 dias juntos; faltam $5 dias para o Nível $6." },
            { "weddingtime_celebration", "Parabéns a $1 e $2! O Laço do Casamento deles chegou ao Nível $3!" },

            { "weddingtime_not_married", "Você não é casado(a)." },
            { "weddingtime_divorce_warning", "Divorciar-se de $1 encerra o Laço do Casamento e apaga o progresso de vocês.$2 Digite /divorce confirm em até $3 segundos para continuar." },
            { "weddingtime_divorce_warning_wait", " O divórcio se torna definitivo $1 depois da confirmação; qualquer um dos dois pode cancelar antes disso." },
            { "weddingtime_nothing_to_confirm", "Nada para confirmar. Digite /divorce primeiro." },
            { "weddingtime_request_sent", "Pedido enviado." },
            { "weddingtime_divorce_requested", "Divórcio solicitado. Ele se torna definitivo $1. Use /divorce cancel para interrompê-lo." },
            { "weddingtime_divorce_requested_partner", "$1 pediu o divórcio. Ele se torna definitivo $2. Use /divorce cancel para interrompê-lo." },
            { "weddingtime_divorce_already_pending", "Já existe um divórcio pendente. Use /divorce cancel para interrompê-lo." },
            { "weddingtime_divorce_cancelled", "O divórcio foi cancelado." },
            { "weddingtime_no_pending_divorce", "Não há divórcio pendente." },
            { "weddingtime_marriage_ended", "Seu casamento terminou." },
            { "weddingtime_admin_only", "Somente administradores do servidor podem forçar um divórcio." },
            { "weddingtime_admin_ended", "Um administrador do servidor encerrou este casamento." },
            { "weddingtime_admin_done", "O casamento de $1 foi encerrado." },
            { "weddingtime_target_not_found", "Nenhum jogador online chamado '$1' foi encontrado. Use o ID do jogador se ele estiver offline." },
            { "weddingtime_target_ambiguous", "Há mais de um jogador online chamado '$1'. Use o ID do jogador." },
            { "weddingtime_target_not_married", "Esse jogador não é casado." },

            { "weddingtime_duration_minutes", "em $1 minutos" },
            { "weddingtime_duration_minute", "em 1 minuto" },
            { "weddingtime_duration_seconds", "em $1 segundos" },
        };
    }
}
