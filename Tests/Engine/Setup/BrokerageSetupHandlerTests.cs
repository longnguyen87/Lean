﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.RealTime;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Lean.Engine.Setup;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using HistoryRequest = QuantConnect.Data.HistoryRequest;

namespace QuantConnect.Tests.Engine.Setup
{
    [TestFixture]
    public class BrokerageSetupHandlerTests
    {
        private IAlgorithm _algorithm;
        private ITransactionHandler _transactionHandler;
        private NonDequeingTestResultsHandler _resultHandler;
        private IBrokerage _brokerage;

        private TestableBrokerageSetupHandler _brokerageSetupHandler;

        [TestFixtureSetUp]
        public void Setup()
        {
            _algorithm = new QCAlgorithm();
            _algorithm.SubscriptionManager.SetDataManager(new DataManager());
            _transactionHandler = new BrokerageTransactionHandler();
            _resultHandler = new NonDequeingTestResultsHandler();
            _brokerage = new TestBrokerage();

            _brokerageSetupHandler = new TestableBrokerageSetupHandler();
        }

        [Test]
        public void CanGetOpenOrders()
        {
            _brokerageSetupHandler.PublicGetOpenOrders(_algorithm, _resultHandler, _transactionHandler, _brokerage);

            Assert.AreEqual(_transactionHandler.Orders.Count, 4);

            Assert.AreEqual(_transactionHandler.OrderTickets.Count, 4);

            // Warn the user about each open order
            Assert.AreEqual(_resultHandler.PersistentMessages.Count, 4);

            // Market order
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Market).Value.Quantity, 100);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Market).Value.SubmitRequest.LimitPrice, 1.2345m);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Market).Value.SubmitRequest.StopPrice, 1.2345m);

            // Limit Order
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Limit).Value.Quantity, -100);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Limit).Value.SubmitRequest.LimitPrice, 2.2345m);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.Limit).Value.SubmitRequest.StopPrice, 0m);

            // Stop market order
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopMarket).Value.Quantity, 100);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopMarket).Value.SubmitRequest.LimitPrice, 0m);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopMarket).Value.SubmitRequest.StopPrice, 2.2345m);

            // Stop Limit order
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopLimit).Value.Quantity, 100);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopLimit).Value.SubmitRequest.LimitPrice, 0.2345m);
            Assert.AreEqual(_transactionHandler.OrderTickets.First(x => x.Value.OrderType == OrderType.StopLimit).Value.SubmitRequest.StopPrice, 2.2345m);

            // SPY security should be added to the algorithm
            Assert.Contains(Symbols.SPY, _algorithm.Securities.Select(x => x.Key).ToList());
        }

        [Test, TestCaseSource(nameof(GetExistingHoldingsAndOrdersTestCaseData))]
        public void LoadsExistingHoldingsAndOrders(List<Holding> holdings, List<Order> orders)
        {
            var algorithm = new TestAlgorithm();
            var job = new LiveNodePacket { UserId = 1, ProjectId = 1, DeployId = "1", Brokerage = "PaperBrokerage", DataQueueHandler = "none"};
            var resultHandler = new Mock<IResultHandler>();
            var transactionHandler = new Mock<ITransactionHandler>();
            var realTimeHandler = new Mock<IRealTimeHandler>();
            var objectStore = new Mock<IObjectStore>();

            var brokerage = new Mock<IBrokerage>();
            brokerage.Setup(x => x.IsConnected).Returns(true);
            brokerage.Setup(x => x.GetCashBalance()).Returns(new List<Cash>());
            brokerage.Setup(x => x.GetAccountHoldings()).Returns(holdings);
            brokerage.Setup(x => x.GetOpenOrders()).Returns(orders);

            var setupHandler = new BrokerageSetupHandler();

            IBrokerageFactory factory;
            setupHandler.CreateBrokerage(job, algorithm, out factory);
            var result = setupHandler.Setup(algorithm, brokerage.Object, job, resultHandler.Object, transactionHandler.Object, realTimeHandler.Object, objectStore.Object);

            Assert.IsTrue(result);
        }

        public TestCaseData[] GetExistingHoldingsAndOrdersTestCaseData()
        {
            return new []
            {
                new TestCaseData(new List<Holding>(), new List<Order>())
                    .SetName("None"),

                new TestCaseData(
                    new List<Holding>
                    {
                        new Holding { Type = SecurityType.Equity, Symbol = Symbols.SPY, Quantity = 1 }
                    },
                    new List<Order>
                    {
                        new LimitOrder(Symbols.SPY, 1, 1, DateTime.UtcNow)
                    })
                    .SetName("Equity"),

                new TestCaseData(
                    new List<Holding>
                    {
                        new Holding { Type = SecurityType.Option, Symbol = Symbols.SPY_C_192_Feb19_2016, Quantity = 1 }
                    },
                    new List<Order>
                    {
                        new LimitOrder(Symbols.SPY_C_192_Feb19_2016, 1, 1, DateTime.UtcNow)
                    })
                    .SetName("Option"),

                new TestCaseData(
                        new List<Holding>
                        {
                            new Holding { Type = SecurityType.Equity, Symbol = Symbols.SPY, Quantity = 1 },
                            new Holding { Type = SecurityType.Option, Symbol = Symbols.SPY_C_192_Feb19_2016, Quantity = 1 }
                        },
                        new List<Order>
                        {
                            new LimitOrder(Symbols.SPY, 1, 1, DateTime.UtcNow),
                            new LimitOrder(Symbols.SPY_C_192_Feb19_2016, 1, 1, DateTime.UtcNow)
                        })
                    .SetName("Equity + Option"),

                new TestCaseData(
                        new List<Holding>
                        {
                            new Holding { Type = SecurityType.Option, Symbol = Symbols.SPY_C_192_Feb19_2016, Quantity = 1 },
                            new Holding { Type = SecurityType.Equity, Symbol = Symbols.SPY, Quantity = 1 }
                        },
                        new List<Order>
                        {
                            new LimitOrder(Symbols.SPY_C_192_Feb19_2016, 1, 1, DateTime.UtcNow),
                            new LimitOrder(Symbols.SPY, 1, 1, DateTime.UtcNow)
                        })
                    .SetName("Option + Equity"),

                new TestCaseData(
                        new List<Holding>
                        {
                            new Holding { Type = SecurityType.Forex, Symbol = Symbols.EURUSD, Quantity = 1 }
                        },
                        new List<Order>
                        {
                            new LimitOrder(Symbols.EURUSD, 1, 1, DateTime.UtcNow)
                        })
                    .SetName("Forex"),

                new TestCaseData(
                        new List<Holding>
                        {
                            new Holding { Type = SecurityType.Crypto, Symbol = Symbols.BTCUSD, Quantity = 1 }
                        },
                        new List<Order>
                        {
                            new LimitOrder(Symbols.BTCUSD, 1, 1, DateTime.UtcNow)
                        })
                    .SetName("Crypto"),

                new TestCaseData(
                        new List<Holding>
                        {
                            new Holding { Type = SecurityType.Future, Symbol = Symbols.Fut_SPY_Feb19_2016, Quantity = 1 }
                        },
                        new List<Order>
                        {
                            new LimitOrder(Symbols.Fut_SPY_Feb19_2016, 1, 1, DateTime.UtcNow)
                        })
                    .SetName("Future"),
            };
        }

        private class TestAlgorithm : QCAlgorithm
        {
            public TestAlgorithm()
            {
                SubscriptionManager.SetDataManager(new DataManager());
            }

            public override void Initialize() {}
        }

        private class NonDequeingTestResultsHandler : TestResultHandler
        {
            private readonly AlgorithmNodePacket _job = new BacktestNodePacket();
            public readonly ConcurrentQueue<Packet> PersistentMessages  = new ConcurrentQueue<Packet>();

            public override void DebugMessage(string message)
            {
                PersistentMessages.Enqueue(new DebugPacket(_job.ProjectId, _job.AlgorithmId, _job.CompileId, message));
            }
        }

        private class TestableBrokerageSetupHandler : BrokerageSetupHandler
        {
            private readonly HashSet<SecurityType> _supportedSecurityTypes = new HashSet<SecurityType>
            {
                SecurityType.Equity, SecurityType.Forex, SecurityType.Cfd, SecurityType.Option, SecurityType.Future, SecurityType.Crypto
            };

            public void PublicGetOpenOrders(IAlgorithm algorithm, IResultHandler resultHandler, ITransactionHandler transactionHandler, IBrokerage brokerage)
            {
                GetOpenOrders(algorithm, resultHandler, transactionHandler, brokerage, _supportedSecurityTypes, Resolution.Second);
            }
        }
    }

    internal class TestBrokerage : IBrokerage
    {
        public event EventHandler<OrderEvent> OrderStatusChanged;
        public event EventHandler<OrderEvent> OptionPositionAssigned;
        public event EventHandler<AccountEvent> AccountChanged;
        public event EventHandler<BrokerageMessageEvent> Message;
        public string Name { get; }
        public bool IsConnected { get; }

        public List<Order> GetOpenOrders()
        {
            const decimal delta = 1m;
            const decimal price = 1.2345m;
            const int quantity = 100;
            const decimal pricePlusDelta = price + delta;
            const decimal priceMinusDelta = price - delta;
            var tz = TimeZones.NewYork;

            var time = new DateTime(2016, 2, 4, 16, 0, 0).ConvertToUtc(tz);
            var marketOrderWithPrice = new MarketOrder(Symbols.SPY, quantity, time);
            marketOrderWithPrice.Price = price;

            return new List<Order>()
            {
                marketOrderWithPrice,
                new LimitOrder(Symbols.SPY, -quantity, pricePlusDelta, time),
                new StopMarketOrder(Symbols.SPY, quantity, pricePlusDelta, time),
                new StopLimitOrder(Symbols.SPY, quantity, pricePlusDelta, priceMinusDelta, time)
            };
        }

        #region UnusedMethods
        public void Dispose()
        {
        }
        public List<Holding> GetAccountHoldings()
        {
            throw new NotImplementedException();
        }

        public List<Cash> GetCashBalance()
        {
            throw new NotImplementedException();
        }

        public bool PlaceOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public bool UpdateOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public bool CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void Connect()
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public bool AccountInstantlyUpdated { get; }
        public IEnumerable<BaseData> GetHistory(HistoryRequest request)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
