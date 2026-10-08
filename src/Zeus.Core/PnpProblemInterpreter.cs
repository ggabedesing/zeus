using System.Globalization;

namespace Zeus.Core;

public sealed record PnpProblemInterpretation(uint? Code, string Meaning, string Guidance, bool IsKnown);

/// <summary>Explains selected documented Windows PnP problem codes without diagnosing hardware cause.</summary>
public static class PnpProblemInterpreter
{
    private static readonly IReadOnlyDictionary<uint, (string Meaning, string Guidance)> Known =
        new Dictionary<uint, (string, string)>
        {
            [1] = ("O dispositivo está configurado incorretamente.", "Abra as propriedades no Gerenciador de Dispositivos e revise a mensagem completa."),
            [2] = ("O Windows não conseguiu carregar o driver; a causa exata não está determinada por este código.", "Revise os detalhes do dispositivo e confirme o driver correspondente no Windows Update ou no fabricante oficial."),
            [3] = ("O Windows informa possível falha no driver ou falta de recursos do sistema.", "Confira o Gerenciador de Dispositivos e feche programas desnecessários; isso não confirma falta de memória."),
            [4] = ("O Windows informa falha do dispositivo e aponta driver ou Registro como possibilidades.", "Consulte os detalhes do dispositivo e o suporte oficial do fabricante antes de alterar drivers ou Registro."),
            [5] = ("O driver solicita um recurso que o Windows não consegue gerenciar.", "Revise os recursos e conflitos mostrados nas propriedades do dispositivo."),
            [6] = ("A configuração de inicialização do dispositivo conflita com outro dispositivo.", "Revise os recursos e o histórico recente de alterações no Gerenciador de Dispositivos."),
            [8] = ("O carregador de driver necessário não foi encontrado.", "Confira o pacote de driver oficial específico para o modelo e a versão do Windows."),
            [9] = ("O firmware está reportando recursos do dispositivo de forma incorreta, segundo o Windows.", "Consulte o fabricante do dispositivo ou do computador; não altere opções de firmware automaticamente."),
            [10] = ("O dispositivo não pode ser iniciado.", "Revise a mensagem completa, o driver e os eventos do dispositivo; o código não determina sozinho a causa."),
            [12] = ("O Windows não encontrou recursos livres suficientes para o dispositivo.", "Revise conflitos de recursos no Gerenciador de Dispositivos e alterações recentes."),
            [14] = ("O Windows informa que é necessário reiniciar o computador.", "Salve o trabalho e reinicie pelo Windows quando for conveniente; depois confira se o código continua presente."),
            [18] = ("O Windows recomenda reinstalar os drivers deste dispositivo.", "Identifique primeiro o modelo exato e use o Windows Update ou a página oficial do fabricante."),
            [19] = ("O Windows informa possível problema no Registro para este dispositivo.", "Não edite o Registro manualmente; consulte o fabricante ou suporte técnico."),
            [22] = ("O dispositivo está desabilitado.", "Confira se a desativação foi intencional. O ZEUS não habilita o dispositivo automaticamente."),
            [24] = ("O dispositivo pode estar ausente, com falha ou sem todos os drivers, segundo o Windows.", "Se for externo, confira a conexão; se continua conectado, consulte os detalhes e o driver oficial."),
            [28] = ("Os drivers deste dispositivo não estão instalados, segundo o Windows.", "Confira atualizações no Windows Update ou procure o driver para o modelo exato no site oficial do fabricante."),
            [29] = ("O firmware do dispositivo não forneceu os recursos necessários e o Windows o desabilitou.", "Consulte a documentação do fabricante. Não altere opções de firmware ou segurança automaticamente."),
            [30] = ("O dispositivo está usando um recurso IRQ que outro dispositivo também usa.", "Revise os detalhes de recursos no Gerenciador de Dispositivos e consulte o fabricante."),
            [31] = ("O Windows não consegue carregar os drivers necessários para o dispositivo.", "Revise a mensagem completa e confirme um driver oficial compatível com o modelo e o Windows."),
            [32] = ("O serviço de driver associado está desabilitado.", "Não altere o serviço sem identificar sua função e dependências; consulte o fabricante ou suporte."),
            [37] = ("O Windows informa que o driver falhou ao inicializar.", "Revise os detalhes e procure uma versão oficial compatível; código e versão não identificam sozinhos a causa."),
            [39] = ("O Windows não conseguiu carregar o driver do dispositivo.", "Revise os detalhes e confirme a origem e compatibilidade do pacote antes de qualquer instalação."),
            [43] = ("Um driver informou ao Windows que o dispositivo apresentou um problema.", "Consulte os detalhes e eventos do dispositivo; se persistir, use o suporte oficial do fabricante."),
            [45] = ("O Windows informa que o dispositivo não está conectado no momento.", "Conecte o dispositivo se espera encontrá-lo; dispositivos removidos podem continuar listados como ausentes."),
            [48] = ("O driver foi bloqueado pelo Windows por problemas de compatibilidade, segundo o código.", "Use uma versão compatível obtida de fonte oficial; não contorne o bloqueio de segurança."),
            [52] = ("O Windows não conseguiu verificar a assinatura digital exigida para o driver.", "Procure um driver assinado e oficial; não desative a verificação de assinatura."),
        };

    public static PnpProblemInterpretation Interpret(string? rawCode)
    {
        if (!uint.TryParse(rawCode, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            return new(null, "Código de problema indisponível ou em formato não reconhecido; sem interpretação verificada.", "Confira o estado diretamente no Gerenciador de Dispositivos.", false);
        if (Known.TryGetValue(code, out var result)) return new(code, result.Meaning, result.Guidance, true);
        return new(code, $"O Windows retornou o código de problema {code}; o ZEUS não tem uma interpretação verificada para este código.", "Consulte a mensagem completa no Gerenciador de Dispositivos e a documentação oficial antes de agir.", false);
    }
}
