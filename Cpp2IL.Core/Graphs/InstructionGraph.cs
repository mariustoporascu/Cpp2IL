﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class AbstractControlFlowGraph<TInstruction, TNode> : IControlFlowGraph
    where TNode : InstructionGraphNode<TInstruction>, new()
{
    protected List<TInstruction> Instructions;

    protected TNode ExitNode;

    Dictionary<ulong, TInstruction> InstructionsByAddress;
    protected int idCounter = 0;

    protected AbstractControlFlowGraph(List<TInstruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));


        var startNode = new TNode() {ID = idCounter++};
        ExitNode = new TNode() {ID = idCounter++};
        Instructions = instructions;
        InstructionsByAddress = new Dictionary<ulong, TInstruction>();
        Root = startNode;
        nodeSet = new Collection<TNode>();

        foreach (var instruction in Instructions)
            InstructionsByAddress.Add(GetAddressOfInstruction(instruction), instruction);

    }

    protected virtual ulong GetAddressOfInstruction(TInstruction instruction)
    {
        throw new NotImplementedException();
    }

    protected TNode SplitAndCreate(TNode target, int index)
    {
        if (index < 0 || index >= target.Instructions.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        // Don't need to split...
        if (index == 0)
            return target;

        var newNode = new TNode() {ID = idCounter++};

        // target split in two
        // targetFirstPart -> targetSecondPart aka newNode

        // Take the instructions for the secondPart
        var instructions = target.Instructions.GetRange(index, target.Instructions.Count - index);
        target.Instructions.RemoveRange(index, target.Instructions.Count - index);

        // Add those to the newNode
        newNode.Instructions.AddRange(instructions);
        // Transfer control flow
        newNode.FlowControl = target.FlowControl;
        target.FlowControl = InstructionGraphNodeFlowControl.Continue;

        // Transfer successors
        newNode.Successors = target.Successors;
        newNode.HasProcessedSuccessors = target.HasProcessedSuccessors;
        newNode.NeedsCorrectingDueToJump = target.NeedsCorrectingDueToJump;
        target.NeedsCorrectingDueToJump = false; //We've split, so this no longer ends with a jump
        target.Successors = new();

        // Correct the predecessors for all the successors
        foreach (var successor in newNode.Successors)
        {
            for (int i = 0; i < successor.Predecessors.Count; i++)
            {
                if (successor.Predecessors[i] == target)
                    successor.Predecessors[i] = newNode;
            }
        }

        // Add newNode and connect it
        AddNode(newNode);
        AddDirectedEdge(target, newNode);

        return newNode;
    }

    private static Dictionary<Instruction, InstructionGraphUseDef> UsageAndDefinitions = new();


    public void Run(bool print = false)
    {
        AddNode(Root);
        if (Instructions.Count == 0)
        {
            AddNode(ExitNode);
            AddDirectedEdge(Root, ExitNode);
            return;
        }

        BuildInitialGraph();
        AddNode(ExitNode);
        SegmentGraph();
        ConstructConditions();
        ComputeDominators();
        IdentifyLoops();
        IdentifyIfs();
        CalculateUseDefs();
        DetermineLocals();
        if (print)
            Console.WriteLine(Print());
    }


    private void IdentifyIfs()
    {
        TraverseAndPostExecute(Root, node =>
        {
            bool success = false;
            do
            {
                success = TrySquash(node);
            } while (success);
        });
    }

    private void TraverseAndPostExecute(InstructionGraphNode<TInstruction> node,
        Action<InstructionGraphNode<TInstruction>> action)
    {
        foreach (var successor in node.Successors)
            TraverseAndPostExecute(successor, action);

        action(node);
    }

    private bool TrySquash(InstructionGraphNode<TInstruction> node)
    {
        if(node == ExitNode || node == Root)
            return false;

        if (node.Successors.Count == 1)
        {
            if (node.Successors[0].Predecessors.Count == 1)
            {
                var succ = (TNode) node.Successors[0];
                node.FlowControl = succ.FlowControl;
                node.Instructions.AddRange(succ.Instructions);
                DirectedEdgeRemove(node, succ);
                node.Successors.Clear();
                node.Successors = succ.Successors;
                nodeSet.Remove(succ);
                return true;
            }
        }
        else if (node.Successors.Count == 2)
        {
            
        }

        return false;
    }
    
    protected void DirectedEdgeRemove(InstructionGraphNode<TInstruction> from, InstructionGraphNode<TInstruction> to)
    {
        from.Successors.Remove(to);
        to.Predecessors.Remove(from);
    }

    private bool TrySquashConditional(InstructionGraphNode<TInstruction> node)
    {
        if (!node.IsConditionalBranch)
            return false;
        var condition = node.Condition;
        var truePath = node.TrueTarget;
        var falsePath = node.FalseTarget;
        if (truePath == null || falsePath == null)
            throw new NullReferenceException("True or false path is null, this shouldn't happen ever");
        var truePathSuccessor = GetSingleSucc(truePath);
        var falsePathSuccessor = GetSingleSucc(falsePath);
        if (falsePathSuccessor == truePath)
        {
            // 
            // if (node.condition)
            //   Print false path.
        }
        
        return false;
    }

    //                                         // Haha, yes very funny.... not!
    private InstructionGraphNode<TInstruction>? GetSingleSucc(InstructionGraphNode<TInstruction> node)
    {
        if (node.Successors.Count != 1)
            return null;
        
        return node.Successors[0];
    }


    protected virtual void DetermineLocals()
    {
        throw new NotImplementedException();
    }

    private void ConstructConditions()
    {
        foreach (var graphNode in Nodes)
        {
            if (graphNode.IsConditionalBranch)
            {
                graphNode.CheckCondition();
            }
        }
    }

    private void CalculateUseDefs()
    {
        foreach (var node in Nodes)
        {
            foreach (var instruction in node.Instructions)
            {
                var useDef = new InstructionGraphUseDef();
                GetUseDefsForInstruction(instruction, useDef);
            }
        }
    }

    protected virtual void GetUseDefsForInstruction(TInstruction instruction,
        InstructionGraphUseDef instructionGraphUseDef)
    {
        throw new NotImplementedException();
    }

    protected virtual void SegmentGraph()
    {
        throw new NotImplementedException();
    }

    protected virtual void BuildInitialGraph()
    {
        throw new NotImplementedException();
    }

    // Highly recommend reading https://www.backerstreet.com/decompiler/loop_analysis.php
    private void ComputeDominators()
    {
        for (int i = 0; i < nodeSet.Count; i++)
        {
            nodeSet[i].Dominators = new BitArray(nodeSet.Count);
            nodeSet[i].Dominators!.SetAll(true);
            nodeSet[i].ID = i;
        }

        Root.Dominators!.SetAll(false);

        Root.Dominators.Set(Root.ID, true);

        BitArray temp = new BitArray(nodeSet.Count);

        bool changed = false;
        do
        {
            changed = false;
            foreach (var node in nodeSet)
            {
                if (node == Root)
                    continue;

                foreach (var predecessor in node.Predecessors)
                {
                    temp.SetAll(false);
                    temp.Or(node.Dominators!);
                    node.Dominators!.And(predecessor.Dominators!);
                    node.Dominators.Set(node.ID, true);
                    if (!node.Dominators.BitsAreEqual(temp))
                        changed = true;
                }
            }
        } while (changed);
    }

    private void IdentifyLoops()
    {
        List<InstructionGraphLoop<InstructionGraphNode<TInstruction>>> identifiedLoops = new();
        foreach (var node in nodeSet)
        {
            if (node == Root)
                continue;
            foreach (var succ in node.Successors)
            {
                if (node.Dominators!.Get(succ.ID))
                {
                    identifiedLoops.Add(GetLoopForEdge(succ, node));
                }
            }
        }

        identifiedLoops = SortLoops(identifiedLoops);
        foreach (var loop in identifiedLoops)
        {
            // All loops can be represented as doWhiles for example https://i.imgur.com/fCzStwX.png
            throw new NotImplementedException("Loops aren't currently implemented");
            var doWhileStatement = new InstructionGraphStatement<TInstruction>(InstructionGraphStatementType.DoWhile);
            if (loop.Nodes.Count == 1)
            {
                doWhileStatement.Expression = loop.Nodes[0].Condition;
                //doWhileStatement.Nodes
            }
            //doWhileStatement.Expression = loop.Nodes[1].Condition;
            //doWhileStatement.Nodes = loop.Nodes;
            //doWhileStatement.ContinueNode = loop.Nodes[1];
            //doWhileStatement.

            //TODO: Extract break and continue statement
        }
    }


    private List<InstructionGraphLoop<InstructionGraphNode<TInstruction>>> SortLoops(
        List<InstructionGraphLoop<InstructionGraphNode<TInstruction>>> loops)
    {
        // We'll just assume for now theres only one loop
        // TODO: Return the loops in a list from innermost to outermost
        return loops;
    }

    private InstructionGraphLoop<InstructionGraphNode<TInstruction>> GetLoopForEdge(
        InstructionGraphNode<TInstruction> header, InstructionGraphNode<TInstruction> tail)
    {
        Stack<InstructionGraphNode<TInstruction>> stack = new();
        InstructionGraphLoop<InstructionGraphNode<TInstruction>> loop = new(header);
        if (header != tail)
        {
            loop.Nodes.Add(tail);
            stack.Push(tail);
        }

        while (stack.Count != 0)
        {
            var node = stack.Pop();
            foreach (var predecessor in node.Predecessors)
            {
                if (!loop.Nodes.Contains(predecessor))
                {
                    loop.Nodes.Add(predecessor);
                    stack.Push(predecessor);
                }
            }
        }

        return loop;
    }


    protected TNode? FindNodeByAddress(ulong address)
    {
        if (InstructionsByAddress.TryGetValue(address, out var instruction))
        {
            foreach (var node in Nodes)
            {
                if (node.Instructions.Any(instr => GetAddressOfInstruction(instr) == address))
                {
                    return node;
                }
            }
        }

        return null;
    }

    public TNode Root { get; }

    private Collection<TNode> nodeSet;

    protected void AddDirectedEdge(TNode from, TNode to)
    {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
    }

    protected void AddNode(TNode node) => nodeSet.Add(node);


    public string Print()
    {
        var sb = new StringBuilder();
        foreach (var node in nodeSet)
        {
            sb.Append("=========================\n");
            sb.Append(
                $"ID: {node.ID}, FC: {node.FlowControl}, Successors:{string.Join(",", node.Successors.Select(i => i.ID))}, Predecessors:{string.Join(",", node.Predecessors.Select(i => i.ID))}");
            if (node.IsConditionalBranch)
                sb.Append($", Condition: {node.Condition?.ConditionString ?? "Null"}");
            if (node.Instructions.Count > 0)
                sb.Append($", Address {node.GetFormattedInstructionAddress(node.Instructions.First())}");
            sb.Append("\n");
            foreach (var v in node.Instructions)
            {
                sb.Append(v).Append("\n");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    protected Collection<TNode> Nodes => nodeSet;

    public List<IControlFlowNode> INodes => Nodes.Cast<IControlFlowNode>().ToList();

    public int Count => nodeSet.Count;
}