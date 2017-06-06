﻿using System;
using System.Threading.Tasks;
using Adaptive.ReactiveTrader.Common;
using Adaptive.ReactiveTrader.Contract;
using Adaptive.ReactiveTrader.EventStore.Domain;
using Adaptive.ReactiveTrader.Server.TradeExecution.Commands;
using Adaptive.ReactiveTrader.Server.TradeExecution.Domain;

namespace Adaptive.ReactiveTrader.Server.TradeExecution
{
    public static class CommandHandlers
    {
        public static async Task<ExecuteTradeResponseDto> HandleAsync(ExecuteTradeRequestDto request,
                                                                      string user,
                                                                      IAggregateRepository repository,
                                                                      TradeIdProvider idProvider)
        {
            var tradeId = await idProvider.GetNextId();
            var tradeDate = DateTime.UtcNow;

            DateTime valueDate;
            if (!DateTime.TryParse(request.ValueDate, out valueDate))
            {
                valueDate = DateTime.UtcNow.AddDays(2).Date.ToWeekday();
            }

            var trade = new Trade(tradeId,
                                  user,
                                  request.CurrencyPair,
                                  request.SpotRate,
                                  tradeDate,
                                  valueDate,
                                  request.Direction,
                                  request.Notional,
                                  request.DealtCurrency);

            await repository.SaveAsync(trade);

            // TODO - fix the client so that we timeout based on a trade status stream
            if (request.CurrencyPair == "EURJPY")
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return new ExecuteTradeResponseDto
            {
                Trade = new TradeDto
                {
                    CurrencyPair = trade.CurrencyPair,
                    Direction = trade.Direction,
                    Notional = trade.Notional,
                    SpotRate = trade.SpotRate,
                    Status = trade.State,
                    TradeDate = tradeDate,
                    ValueDate = valueDate,
                    TradeId = tradeId,
                    TraderName = user,
                    DealtCurrency = trade.DealtCurrency
                }
            };
        }

        public static async Task HandleAsync(ReserveCreditCommand command, IAggregateRepository repository)
        {
            var creditAccount = await repository.GetByIdOrCreateAsync<CreditAccount>(command.AccountName);

            // If this credit account doesn't exist yet, create it.
            // In the real world, there would be some other mechanism for creating a credit account,
            // and we wouldn't create one here. If there was no existing acount, an appropriate event
            // could be raised which would go through the TradeExecutionProcess, which in turn could
            // reject the trade.
            if (creditAccount.Version == -1)
            {
                creditAccount.Create(command.AccountName);
            }

            // TODO - fix the client so that we timeout based on a trade status stream
            if (command.TradeDetails.CurrencyPair == "EURJPY")
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            // Emulate some variable delays based on currency pair.
            switch (command.TradeDetails.CurrencyPair)
            {
                case "GBPUSD":
                    await Task.Delay(TimeSpan.FromSeconds(1.5));
                    break;
                default:
                    await Task.Delay(TimeSpan.FromSeconds(.5));
                    break;
            }

            creditAccount.ReserveCredit(command.TradeDetails);

            await repository.SaveAsync(creditAccount);
        }

        public static async Task HandleAsync(CompleteTradeCommand command, IAggregateRepository repository)
        {
            var trade = await repository.GetByIdAsync<Trade>(command.TradeId);
            trade.Complete();
            await repository.SaveAsync(trade);
        }

        public static async Task HandleAsync(RejectTradeCommand command, IAggregateRepository repository)
        {
            var trade = await repository.GetByIdAsync<Trade>(command.TradeId);
            trade.Reject(command.Reason);
            await repository.SaveAsync(trade);
        }
    }
}