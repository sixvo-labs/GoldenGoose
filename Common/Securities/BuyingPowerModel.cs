/*
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
*/

using System;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using static QuantConnect.StringExtensions;

namespace QuantConnect.Securities
{
    /// <summary>
    /// Provides a base class for all buying power models
    /// </summary>
    public class BuyingPowerModel : IBuyingPowerModel
    {
        private decimal _initialMarginRequirement;
        private decimal _maintenanceMarginRequirement;

        /// <summary>
        /// The percentage used to determine the required unused buying power for the account.
        /// </summary>
        protected decimal RequiredFreeBuyingPowerPercent;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuyingPowerModel"/> with no leverage (1x)
        /// </summary>
        public BuyingPowerModel()
            : this(1m)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuyingPowerModel"/>
        /// </summary>
        /// <param name="initialMarginRequirement">The percentage of an order's absolute cost
        /// that must be held in free cash in order to place the order</param>
        /// <param name="maintenanceMarginRequirement">The percentage of the holding's absolute
        /// cost that must be held in free cash in order to avoid a margin call</param>
        /// <param name="requiredFreeBuyingPowerPercent">The percentage used to determine the required
        /// unused buying power for the account.</param>
        public BuyingPowerModel(
            decimal initialMarginRequirement,
            decimal maintenanceMarginRequirement,
            decimal requiredFreeBuyingPowerPercent
            )
        {
            if (initialMarginRequirement < 0 || initialMarginRequirement > 1)
            {
                throw new ArgumentException("Initial margin requirement must be between 0 and 1");
            }

            if (maintenanceMarginRequirement < 0 || maintenanceMarginRequirement > 1)
            {
                throw new ArgumentException("Maintenance margin requirement must be between 0 and 1");
            }

            if (requiredFreeBuyingPowerPercent < 0 || requiredFreeBuyingPowerPercent > 1)
            {
                throw new ArgumentException("Free Buying Power Percent requirement must be between 0 and 1");
            }

            _initialMarginRequirement = initialMarginRequirement;
            _maintenanceMarginRequirement = maintenanceMarginRequirement;
            RequiredFreeBuyingPowerPercent = requiredFreeBuyingPowerPercent;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuyingPowerModel"/>
        /// </summary>
        /// <param name="leverage">The leverage</param>
        /// <param name="requiredFreeBuyingPowerPercent">The percentage used to determine the required
        /// unused buying power for the account.</param>
        public BuyingPowerModel(decimal leverage, decimal requiredFreeBuyingPowerPercent = 0)
        {
            if (leverage < 1)
            {
                throw new ArgumentException("Leverage must be greater than or equal to 1.");
            }

            if (requiredFreeBuyingPowerPercent < 0 || requiredFreeBuyingPowerPercent > 1)
            {
                throw new ArgumentException("Free Buying Power Percent requirement must be between 0 and 1");
            }

            _initialMarginRequirement = 1 / leverage;
            _maintenanceMarginRequirement = 1 / leverage;
            RequiredFreeBuyingPowerPercent = requiredFreeBuyingPowerPercent;
        }

        /// <summary>
        /// Gets the current leverage of the security
        /// </summary>
        /// <param name="security">The security to get leverage for</param>
        /// <returns>The current leverage in the security</returns>
        public virtual decimal GetLeverage(Security security)
        {
            return 1 / _initialMarginRequirement;
        }

        /// <summary>
        /// Sets the leverage for the applicable securities, i.e, equities
        /// </summary>
        /// <remarks>
        /// This is added to maintain backwards compatibility with the old margin/leverage system
        /// </remarks>
        /// <param name="security"></param>
        /// <param name="leverage">The new leverage</param>
        public virtual void SetLeverage(Security security, decimal leverage)
        {
            if (leverage < 1)
            {
                throw new ArgumentException("Leverage must be greater than or equal to 1.");
            }

            var margin = 1 / leverage;
            _initialMarginRequirement = margin;
            _maintenanceMarginRequirement = margin;
        }

        /// <summary>
        /// Gets the total margin required to execute the specified order in units of the account currency including fees
        /// </summary>
        /// <param name="parameters">An object containing the portfolio, the security and the order</param>
        /// <returns>The total margin in terms of the currency quoted in the order</returns>
        public virtual InitialMargin GetInitialMarginRequiredForOrder(
            InitialMarginRequiredForOrderParameters parameters
            )
        {
            //Get the order value from the non-abstract order classes (MarketOrder, LimitOrder, StopMarketOrder)
            //Market order is approximated from the current security price and set in the MarketOrder Method in QCAlgorithm.

            var fees = parameters.Security.FeeModel.GetOrderFee(
                new OrderFeeParameters(parameters.Security,
                    parameters.Order)).Value;
            var feesInAccountCurrency = parameters.CurrencyConverter.
                ConvertToAccountCurrency(fees).Amount;

            var orderMargin = this.GetInitialMarginRequirement(parameters.Security, parameters.Order.Quantity);

            return orderMargin + Math.Sign(orderMargin) * feesInAccountCurrency;
        }

        /// <summary>
        /// Gets the margin currently allocated to the specified holding
        /// </summary>
        /// <param name="parameters">An object containing the security and holdings quantity/cost/value</param>
        /// <returns>The maintenance margin required for the provided holdings quantity/cost/value</returns>
        public virtual MaintenanceMargin GetMaintenanceMargin(MaintenanceMarginParameters parameters)
        {
            return parameters.AbsoluteHoldingsValue * _maintenanceMarginRequirement;
        }

        /// <summary>
        /// Gets the margin cash available for a trade
        /// </summary>
        /// <param name="portfolio">The algorithm's portfolio</param>
        /// <param name="security">The security to be traded</param>
        /// <param name="direction">The direction of the trade</param>
        /// <returns>The margin available for the trade</returns>
        protected virtual decimal GetMarginRemaining(
            SecurityPortfolioManager portfolio,
            Security security,
            OrderDirection direction
            )
        {
            var totalPortfolioValue = portfolio.TotalPortfolioValue;
            var result = portfolio.GetMarginRemaining(totalPortfolioValue);

            if (direction != OrderDirection.Hold)
            {
                var holdings = security.Holdings;
                //If the order is in the same direction as holdings, our remaining cash is our cash
                //In the opposite direction, our remaining cash is 2 x current value of assets + our cash
                if (holdings.IsLong)
                {
                    switch (direction)
                    {
                        case OrderDirection.Sell:
                            result +=
                                // portion of margin to close the existing position
                                this.GetMaintenanceMargin(security) +
                                // portion of margin to open the new position
                                this.GetInitialMarginRequirement(security, security.Holdings.AbsoluteQuantity);
                            break;
                    }
                }
                else if (holdings.IsShort)
                {
                    switch (direction)
                    {
                        case OrderDirection.Buy:
                            result +=
                                // portion of margin to close the existing position
                                this.GetMaintenanceMargin(security) +
                                // portion of margin to open the new position
                                this.GetInitialMarginRequirement(security, security.Holdings.AbsoluteQuantity);
                            break;
                    }
                }
            }

            result -= totalPortfolioValue * RequiredFreeBuyingPowerPercent;
            return result < 0 ? 0 : result;
        }

        /// <summary>
        /// The margin that must be held in order to increase the position by the provided quantity
        /// </summary>
        /// <param name="parameters">An object containing the security and quantity of shares</param>
        /// <returns>The initial margin required for the provided security and quantity</returns>
        public virtual InitialMargin GetInitialMarginRequirement(InitialMarginParameters parameters)
        {
            var security = parameters.Security;
            var quantity = parameters.Quantity;
            return security.QuoteCurrency.ConversionRate
                * security.SymbolProperties.ContractMultiplier
                * security.Price
                * quantity
                * _initialMarginRequirement;
        }

        /// <summary>
        /// Check if there is sufficient buying power to execute this order.
        /// </summary>
        /// <param name="parameters">An object containing the portfolio, the security and the order</param>
        /// <returns>Returns buying power information for an order</returns>
        public virtual HasSufficientBuyingPowerForOrderResult HasSufficientBuyingPowerForOrder(HasSufficientBuyingPowerForOrderParameters parameters)
        {
            // short circuit the div 0 case
            if (parameters.Order.Quantity == 0)
            {
                return parameters.Sufficient();
            }

            var ticket = parameters.Portfolio.Transactions.GetOrderTicket(parameters.Order.Id);
            if (ticket == null)
            {
                return parameters.Insufficient(
                    $"Null order ticket for id: {parameters.Order.Id}"
                );
            }

            if (parameters.Order.Type == OrderType.OptionExercise)
            {
                // for option assignment and exercise orders we look into the requirements to process the underlying security transaction
                var option = (Option.Option) parameters.Security;
                var underlying = option.Underlying;

                if (option.IsAutoExercised(underlying.Close) && underlying.IsTradable)
                {
                    var quantity = option.GetExerciseQuantity(parameters.Order.Quantity);

                    var newOrder = new LimitOrder
                    {
                        Id = parameters.Order.Id,
                        Time = parameters.Order.Time,
                        LimitPrice = option.StrikePrice,
                        Symbol = underlying.Symbol,
                        Quantity = quantity
                    };

                    // we continue with this call for underlying
                    var parametersForUnderlying = parameters.ForUnderlying(newOrder);

                    var freeMargin = underlying.BuyingPowerModel.GetBuyingPower(parametersForUnderlying.Portfolio, parametersForUnderlying.Security, parametersForUnderlying.Order.Direction);
                    // we add the margin used by the option itself
                    freeMargin += GetMaintenanceMargin(MaintenanceMarginParameters.ForQuantityAtCurrentPrice(option, -parameters.Order.Quantity));

                    var initialMarginRequired = underlying.BuyingPowerModel.GetInitialMarginRequiredForOrder(
                        new InitialMarginRequiredForOrderParameters(parameters.Portfolio.CashBook, underlying, newOrder));

                    return HasSufficientBuyingPowerForOrder(parametersForUnderlying, ticket, freeMargin, initialMarginRequired);
                }

                return parameters.Sufficient();
            }

            return HasSufficientBuyingPowerForOrder(parameters, ticket);
        }

        private HasSufficientBuyingPowerForOrderResult HasSufficientBuyingPowerForOrder(HasSufficientBuyingPowerForOrderParameters parameters, OrderTicket ticket,
            decimal? freeMarginToUse = null, decimal? initialMarginRequired = null)
        {
            // When order only reduces or closes a security position, capital is always sufficient
            if (parameters.Security.Holdings.Quantity * parameters.Order.Quantity < 0 && Math.Abs(parameters.Security.Holdings.Quantity) >= Math.Abs(parameters.Order.Quantity))
            {
                return parameters.Sufficient();
            }

            var freeMargin = freeMarginToUse ?? GetMarginRemaining(parameters.Portfolio, parameters.Security, parameters.Order.Direction);
            var initialMarginRequiredForOrder = initialMarginRequired ?? GetInitialMarginRequiredForOrder(
                new InitialMarginRequiredForOrderParameters(
                    parameters.Portfolio.CashBook, parameters.Security, parameters.Order
            ));

            // pro-rate the initial margin required for order based on how much has already been filled
            var percentUnfilled = (Math.Abs(parameters.Order.Quantity) - Math.Abs(ticket.QuantityFilled)) / Math.Abs(parameters.Order.Quantity);
            var initialMarginRequiredForRemainderOfOrder = percentUnfilled * initialMarginRequiredForOrder;

            if (Math.Abs(initialMarginRequiredForRemainderOfOrder) > freeMargin)
            {
                return parameters.Insufficient(Invariant($"Id: {parameters.Order.Id}, ") +
                    Invariant($"Initial Margin: {initialMarginRequiredForRemainderOfOrder.Normalize()}, ") +
                    Invariant($"Free Margin: {freeMargin.Normalize()}")
                );
            }

            return parameters.Sufficient();
        }

        /// <summary>
        /// Get the maximum market order quantity to obtain a delta in the buying power used by a security.
        /// The deltas sign defines the position side to apply it to, positive long, negative short.
        /// </summary>
        /// <param name="parameters">An object containing the portfolio, the security and the delta buying power</param>
        /// <returns>Returns the maximum allowed market order quantity and if zero, also the reason</returns>
        /// <remarks>Used by the margin call model to reduce the position by a delta percent.</remarks>
        public virtual GetMaximumOrderQuantityResult GetMaximumOrderQuantityForDeltaBuyingPower(
            GetMaximumOrderQuantityForDeltaBuyingPowerParameters parameters)
        {
            var usedBuyingPower = parameters.Security.BuyingPowerModel.GetReservedBuyingPowerForPosition(
                new ReservedBuyingPowerForPositionParameters(parameters.Security)).AbsoluteUsedBuyingPower;

            var signedUsedBuyingPower = usedBuyingPower * (parameters.Security.Holdings.IsLong ? 1 : -1);

            var targetBuyingPower = signedUsedBuyingPower + parameters.DeltaBuyingPower;

            var target = 0m;
            if (parameters.Portfolio.TotalPortfolioValue != 0)
            {
                target = targetBuyingPower / parameters.Portfolio.TotalPortfolioValue;
            }

            return GetMaximumOrderQuantityForTargetBuyingPower(
                new GetMaximumOrderQuantityForTargetBuyingPowerParameters(parameters.Portfolio,
                    parameters.Security,
                    target,
                    parameters.MinimumOrderMarginPortfolioPercentage,
                    parameters.SilenceNonErrorReasons));
        }

        /// <summary>
        /// Get the maximum market order quantity to obtain a position with a given buying power percentage.
        /// Will not take into account free buying power.
        /// </summary>
        /// <param name="parameters">An object containing the portfolio, the security and the target signed buying power percentage</param>
        /// <returns>Returns the maximum allowed market order quantity and if zero, also the reason</returns>
        /// <remarks>This implementation ensures that our resulting holdings is less than the target, but it does not necessarily
        /// maximize the holdings to meet the target. To do that we need a minimizing algorithm that reduces the difference between
        /// the target final margin value and the target holdings margin.</remarks>
        public virtual GetMaximumOrderQuantityResult GetMaximumOrderQuantityForTargetBuyingPower(GetMaximumOrderQuantityForTargetBuyingPowerParameters parameters)
        {
            // this is expensive so lets fetch it once
            var totalPortfolioValue = parameters.Portfolio.TotalPortfolioValue;

            // adjust target buying power to comply with required Free Buying Power Percent
            var signedTargetFinalMarginValue =
                parameters.TargetBuyingPower * (totalPortfolioValue - totalPortfolioValue * RequiredFreeBuyingPowerPercent);

            // if targeting zero, simply return the negative of the quantity
            if (signedTargetFinalMarginValue == 0)
            {
                return new GetMaximumOrderQuantityResult(-parameters.Security.Holdings.Quantity, string.Empty, false);
            }

            // we use initial margin requirement here to avoid the duplicate PortfolioTarget.Percent situation:
            // PortfolioTarget.Percent(1) -> fills -> PortfolioTarget.Percent(1) _could_ detect free buying power if we use Maintenance requirement here
            var signedCurrentUsedMargin = this.GetInitialMarginRequirement(parameters.Security, parameters.Security.Holdings.Quantity);

            // remove directionality, we'll work in the land of absolutes
            var absDifferenceOfMargin = Math.Abs(signedTargetFinalMarginValue - signedCurrentUsedMargin);
            var direction = signedTargetFinalMarginValue > signedCurrentUsedMargin ? OrderDirection.Buy : OrderDirection.Sell;

            // determine the unit price in terms of the account currency
            var utcTime = parameters.Security.LocalTime.ConvertToUtc(parameters.Security.Exchange.TimeZone);
            // determine the margin required for 1 unit, positive since we are working with absolutes
            var absUnitMargin = Math.Abs(this.GetInitialMarginRequirement(parameters.Security, 1));
            if (absUnitMargin == 0)
            {
                return new GetMaximumOrderQuantityResult(0, parameters.Security.Symbol.GetZeroPriceMessage());
            }

            if (!BuyingPowerModelExtensions.AboveMinimumOrderMarginPortfolioPercentage(parameters.Portfolio,
                parameters.MinimumOrderMarginPortfolioPercentage, absDifferenceOfMargin))
            {
                var minimumValue = totalPortfolioValue * parameters.MinimumOrderMarginPortfolioPercentage;
                string reason = null;
                if (!parameters.SilenceNonErrorReasons)
                {
                    reason = $"The target order margin {absDifferenceOfMargin} is less than the minimum {minimumValue}.";
                }
                return new GetMaximumOrderQuantityResult(0, reason, false);
            }

            // Use the following loop to converge on a value that places us under our target allocation when adjusted for fees
            var lastOrderQuantity = 0m;     // For safety check
            var signedTargetHoldingsMargin = 0m;
            var orderFees = 0m;
            var orderQuantity = 0m;

            do
            {
                // Calculate our order quantity
                orderQuantity = GetAmountToOrder(parameters.Security, signedCurrentUsedMargin, signedTargetFinalMarginValue);
                if (orderQuantity == 0)
                {
                    var sign = direction == OrderDirection.Buy ? 1 : -1;
                    return new GetMaximumOrderQuantityResult(0,
                        Invariant($"The order quantity is less than the lot size of {parameters.Security.SymbolProperties.LotSize} ") +
                        Invariant($"and has been rounded to zero. Target order margin {absDifferenceOfMargin * sign}. Order fees ") +
                        Invariant($"{orderFees}. Order quantity {orderQuantity * sign}."),
                        false
                    );
                }

                // generate the order
                var order = new MarketOrder(parameters.Security.Symbol, orderQuantity, utcTime);
                var fees = parameters.Security.FeeModel.GetOrderFee(
                    new OrderFeeParameters(parameters.Security,
                        order)).Value;
                orderFees = parameters.Portfolio.CashBook.ConvertToAccountCurrency(fees).Amount;

                // Update our target portfolio margin allocated when considering fees, then calculate the new FinalOrderMargin
                signedTargetFinalMarginValue = (totalPortfolioValue - orderFees - totalPortfolioValue * RequiredFreeBuyingPowerPercent) * parameters.TargetBuyingPower;
                absDifferenceOfMargin = Math.Abs(signedTargetFinalMarginValue - signedCurrentUsedMargin);

                // Start safe check after first loop, stops endless recursion
                if (lastOrderQuantity == orderQuantity)
                {
                    var message =
                        Invariant($"GetMaximumOrderQuantityForTargetBuyingPower failed to converge on the target margin: {signedTargetFinalMarginValue}; ") +
                        Invariant($"the following information can be used to reproduce the issue. Total Portfolio Cash: {parameters.Portfolio.Cash}; Security : {parameters.Security.Symbol.ID}; ") +
                        Invariant($"Price : {parameters.Security.Close}; Leverage: {parameters.Security.Leverage}; Order Fee: {orderFees}; Lot Size: {parameters.Security.SymbolProperties.LotSize}; ") +
                        Invariant($"Current Holdings: {parameters.Security.Holdings.Quantity} @ {parameters.Security.Holdings.AveragePrice}; Target Percentage: %{parameters.TargetBuyingPower * 100};");

                    // Need to add underlying value to message to reproduce with options
                    if (parameters.Security is Option.Option option)
                    {
                        var underlying = option.Underlying;
                        message += Invariant($" Underlying Security: {underlying.Symbol.ID}; Underlying Price: {underlying.Close}; Underlying Holdings: {underlying.Holdings.Quantity} @ {underlying.Holdings.AveragePrice};");
                    }

                    throw new ArgumentException(message);
                }
                lastOrderQuantity = orderQuantity;

                // Update our target holdings margin
                signedTargetHoldingsMargin = this.GetInitialMarginRequirement(parameters.Security,
                    orderQuantity + parameters.Security.Holdings.Quantity);
            }
            // Ensure that our target holdings margin will be less than or equal to our target allocated margin
            while (Math.Abs(signedTargetHoldingsMargin) > Math.Abs(signedTargetFinalMarginValue));

            // add directionality back in
            return new GetMaximumOrderQuantityResult(orderQuantity);
        }

        /// <summary>
        /// Helper function that determines the amount to order to get to a given target safely.
        /// Meaning it will either be at or just below target always.
        /// </summary>
        /// <param name="security">Security we are determine order size for</param>
        /// <param name="currentMargin">Current margin</param>
        /// <param name="targetMargin">Target margin</param>
        /// <returns>The size of the order to get safely to our target</returns>
        public decimal GetAmountToOrder(Security security, decimal currentMargin, decimal targetMargin)
        {
            if (security == null)
            {
                return 0;
            }

            var lotSize = security.SymbolProperties.LotSize;

            // Use the margin for one unit to make our initial guess.
            var marginForOneUnit = Math.Abs(this.GetInitialMarginRequirement(security, 1));

            // For shorting cases we need to see the margin for one shorted unit as well.
            if (targetMargin < 0)
            {
                var marginForOneShortedUnit = this.GetInitialMarginRequirement(security, -1);

                // Easy case, margin for one shorted unit is larger (greater negative number) than our target
                // Just sell all of the current holdings
                if (marginForOneShortedUnit < targetMargin)
                {
                    return -security.Holdings.Quantity;
                }
            }

            // Take a first best guess using margin for one unit to determine order size
            var orderSize = (targetMargin - currentMargin) / marginForOneUnit;

            // Determine if we are under or over our target
            // For negative target, we are under if target is a larger negative number than current
            // For positive target, we are under if target is a larger positive number that current
            var underTarget =
                (targetMargin < 0 && targetMargin - currentMargin < 0) ||
                (targetMargin > 0 && targetMargin - currentMargin > 0);

            // Determine the rounding mode for this order size
            var roundingMode = underTarget
                // Increase in holdings
                // Negative orders need to be rounded towards positive so we don't go over target
                // Positive orders need to be rounded towards negative so we don't go over target
                ? orderSize < 0 ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToNegativeInfinity

                // Reduction of holdings
                // Negative orders need to be rounded towards negative so we are under our target
                // Positive orders need to be rounded towards positive so we are under our target
                : orderSize < 0 ? MidpointRounding.ToNegativeInfinity : MidpointRounding.ToPositiveInfinity;


            // Round this order size appropriately
            orderSize = orderSize.DiscretelyRoundBy(lotSize, roundingMode);

            // Use our model to calculate this final margin as a final check
            var finalMargin = this.GetInitialMarginRequirement(security,
                    orderSize + security.Holdings.Quantity);

            // Until our absolute final margin is equal to or below target we need to adjust  
            // This isn't usually the case, but for non-linear margin per unit cases this may be necessary.
            while ((targetMargin < 0 && finalMargin < targetMargin) || (targetMargin > 0 && finalMargin > targetMargin))
            {
                // We adjust according to the target margin being a short or long
                orderSize = targetMargin < 0
                    ? orderSize + lotSize
                    : orderSize - lotSize;

                // Recalculate final margin with this adjusted orderSize
                finalMargin = this.GetInitialMarginRequirement(security,
                    orderSize + security.Holdings.Quantity);
            }

            return orderSize;
        }

        /// <summary>
        /// Gets the amount of buying power reserved to maintain the specified position
        /// </summary>
        /// <param name="parameters">A parameters object containing the security</param>
        /// <returns>The reserved buying power in account currency</returns>
        public virtual ReservedBuyingPowerForPosition GetReservedBuyingPowerForPosition(ReservedBuyingPowerForPositionParameters parameters)
        {
            var maintenanceMargin = this.GetMaintenanceMargin(parameters.Security);
            return parameters.ResultInAccountCurrency(maintenanceMargin);
        }

        /// <summary>
        /// Gets the buying power available for a trade
        /// </summary>
        /// <param name="parameters">A parameters object containing the algorithm's portfolio, security, and order direction</param>
        /// <returns>The buying power available for the trade</returns>
        public virtual BuyingPower GetBuyingPower(BuyingPowerParameters parameters)
        {
            var marginRemaining = GetMarginRemaining(parameters.Portfolio, parameters.Security, parameters.Direction);
            return parameters.ResultInAccountCurrency(marginRemaining);
        }
    }
}
