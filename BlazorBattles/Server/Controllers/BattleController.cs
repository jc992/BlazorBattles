using BlazorBattles.Server.Data;
using BlazorBattles.Server.Services;
using BlazorBattles.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlazorBattles.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BattleController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly IUtilityService _utilityService;

        public BattleController(
            DataContext context,
            IUtilityService utilityService)
        {
            _context = context;
            _utilityService = utilityService;
        }

        [HttpPost]
        public async Task<IActionResult> StartBattle([FromBody] int opponentId)
        {
            var attacker = await _utilityService.GetUser();
            var opponent = await _context.Users.FindAsync(opponentId);

            if (opponent == null || opponent.IsDeleted) return NotFound("Opponent not available.");

            var result = new BattleResult();
            await Fight(attacker, opponent, result);


            return Ok(result);
        }

        private async Task Fight(User attacker, User opponent, BattleResult result)
        {
            var attackerArmy = await _context.UserUnits
                .Where(un => un.UserId == attacker.Id && un.HitPoints > 0)
                .Include(un => un.Unit)
                .ToListAsync();

            var opponentArmy = await _context.UserUnits
                .Where(un => un.UserId == opponent.Id && un.HitPoints > 0)
                .Include(un => un.Unit)
                .ToListAsync();

            var attackerDamageSum = 0;
            var opponentDamageSum = 0;

            int currentRound = 0;
            while (attackerArmy.Count > 0 && opponentArmy.Count > 0)
            {
                currentRound++;

                if (currentRound % 2 != 0)
                    attackerDamageSum += FightRound(attacker, opponent, attackerArmy, opponentArmy, result);
                else
                    opponentDamageSum += FightRound(opponent, attacker, opponentArmy, attackerArmy, result);
            }

            result.IsVictory = opponentArmy.Count == 0;
            result.RoundsFought = currentRound;

            if (result.RoundsFought > 0)
                await FinishFight(attacker, opponent, result, attackerDamageSum, opponentDamageSum);
        }

        private int FightRound(User aggressor, User defendor,
            List<UserUnit> aggressorArmy, List<UserUnit> defendorArmy, BattleResult result)
        {
            int randomAggressorIndex = new Random().Next(aggressorArmy.Count);
            int randomDefendorIndex = new Random().Next(defendorArmy.Count);

            var randomAggressor = aggressorArmy[randomAggressorIndex];
            var randomDefendor = defendorArmy[randomDefendorIndex];

            var damage =
                new Random().Next(randomAggressor.Unit.Attack) - new Random().Next(randomDefendor.Unit.Defense);
            if (damage < 0) damage = 0;

            if (damage <= randomDefendor.HitPoints)
            {
                randomDefendor.HitPoints -= damage;
                result.Log.Add(
                    $"{aggressor.Username}'s {randomAggressor.Unit.Title} attacks " +
                    $"{defendor.Username}'s {randomDefendor.Unit.Title} with {damage} damage.");

                return damage;
            }
            else
            {
                damage = randomDefendor.HitPoints;
                randomDefendor.HitPoints = 0;
                defendorArmy.Remove(randomDefendor);
                result.Log.Add(
                    $"{aggressor.Username}'s {randomAggressor.Unit.Title} kills " +
                    $"{defendor.Username}'s {randomDefendor.Unit.Title}!");

                return damage;
            }
        }

        private async Task FinishFight(User attacker, User opponent, BattleResult result,
            int attackerDamageSum, int opponentDamageSum)
        {
            result.AttackerDamageSum = attackerDamageSum;
            result.OpponentDamageSum = opponentDamageSum;

            attacker.Battles++;
            opponent.Battles++;

            if (result.IsVictory)
            {
                attacker.Victories++;
                opponent.Defeats++;
                attacker.Bananas += opponentDamageSum;
                opponent.Bananas += attackerDamageSum * 10;
            }
            else
            {
                attacker.Defeats++;
                opponent.Victories++;
                attacker.Bananas += opponentDamageSum * 10;
                opponent.Bananas += attackerDamageSum;
            }

            StoreBattleHistory(attacker, opponent, result);
            await _context.SaveChangesAsync();
        }

        private void StoreBattleHistory(User attacker, User opponent, BattleResult result)
        {
            var battle = new Battle()
            {
                Attacker = attacker,
                Opponent = opponent,
                RoundsFought = result.RoundsFought,
                WinnerDamage = result.IsVictory ? result.AttackerDamageSum : result.OpponentDamageSum,
                Winner = result.IsVictory ? attacker : opponent,
            };

            _context.Battles.Add(battle);
        }
    }
}
