# Plano de implementação — ocorrências e manutenção

1. Consolidar contratos compartilhados para painel, ocorrência, ordem de serviço, SLA, prestador, preventiva e anexos.
2. Estender o domínio e o modelo EF Core com isolamento por licença, índices, relacionamentos e migração.
3. Criar a proteção reutilizável do módulo `Incidents` e aplicá-la também às APIs existentes.
4. Implementar serviços de SLA, numeração, geração preventiva idempotente e auditoria.
5. Implementar APIs de manutenção para equipe e APIs restritas para moradores.
6. Evoluir o cliente HTTP compartilhado.
7. Refazer o módulo do portal como painel operacional e adicionar diálogos de OS, preventiva e prestador.
8. Criar a experiência mobile para abertura e acompanhamento de chamados e ligá-la ao menu condicionado ao módulo.
9. Adicionar testes de segurança, isolamento, SLA, recorrência e contratos de endpoint.
10. Gerar e aplicar a migração; executar builds, testes e validação visual/funcional no portal.
