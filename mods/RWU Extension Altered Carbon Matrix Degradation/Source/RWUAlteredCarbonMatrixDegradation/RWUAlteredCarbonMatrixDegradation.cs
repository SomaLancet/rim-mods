using System.Collections.Generic;
using AlteredCarbon;
using UnityEngine;
using Verse;

namespace RWUAlteredCarbonMatrixDegradation
{
    public sealed class CompProperties_MatrixDegradationRecovery : CompProperties
    {
        public CompProperties_MatrixDegradationRecovery()
        {
            compClass = typeof(CompMatrixDegradationRecovery);
        }
    }

    public sealed class CompMatrixDegradationRecovery : ThingComp
    {
        // AlteredCarbon.Hediff_StackDegradation.Tick subtracts this exact value each tick.
        private const float RecoveryPerTick = 1.66666666E-07f;

        private static readonly HashSet<NeuralStack> ProcessedStacks = new HashSet<NeuralStack>();
        private static int processedAtTick = -1;

        public override void CompTick()
        {
            var matrix = parent as Building_NeuralMatrix;
            if (matrix == null || !matrix.Powered)
            {
                return;
            }

            var currentTick = Find.TickManager.TicksGame;
            if (processedAtTick != currentTick)
            {
                ProcessedStacks.Clear();
                processedAtTick = currentTick;
            }

            foreach (var stack in matrix.AllNeuralStacks)
            {
                if (stack == null || !ProcessedStacks.Add(stack))
                {
                    continue;
                }

                var neuralData = stack.NeuralData;
                if (neuralData == null || neuralData.stackDegradation <= 0f)
                {
                    continue;
                }

                neuralData.stackDegradation = Mathf.Max(
                    0f,
                    neuralData.stackDegradation - RecoveryPerTick);
            }
        }
    }
}
