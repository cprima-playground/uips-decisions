# justfile — UiPath decision workflows
#
# Usage:
#   just                              list available recipes
#   just pack                         pack only (validate XAML, no execution)
#   just run-routing                  pack + run RoutingPipeline wrapper
#   just run-eligibility IfElse       pack + run EligibilityDecision wrapper
#   just run ENTRY                    pack + run arbitrary entry point
#
# Packages land in out/ (gitignored); version is auto-incremented.

set windows-shell := ["pwsh", "-NonInteractive", "-Command"]

_script := "scripts\\pack_and_run.ps1"

# List available recipes
default:
    @just --list

# Pack only — validate XAML, no execution
pack:
    pwsh -NonInteractive -File {{_script}} -PackOnly

# Pack and run the RoutingPipeline end-to-end wrapper
run-routing:
    pwsh -NonInteractive -File {{_script}} \
        -EntryPoint "Tests\RoutingPipeline\Workflow_RoutingPipeline_Pipeline.xaml"

# Pack and run an EligibilityDecision wrapper  (default: IfElse)
# Example: just run-eligibility DecisionTable
run-eligibility pattern="IfElse":
    pwsh -NonInteractive -File {{_script}} \
        -EntryPoint "Tests\EligibilityDecision\Workflow_EligibilityDecision_{{pattern}}.xaml"

# Pack and run an arbitrary entry point
# Example: just run "Tests\EligibilityDecision\Workflow_EligibilityDecision_StateMachine.xaml"
run entry:
    pwsh -NonInteractive -File {{_script}} -EntryPoint "{{entry}}"

# Walk all implementation XAML files and emit the activity tree to workflow_tree_output.txt
tree:
    dotnet run --file scripts/workflow_tree.cs
