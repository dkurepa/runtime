/**
 * \file Runtime options
 *
 * Copyright 2020 Microsoft
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

/*
 * This file defines all the flags/options which can be set at runtime.
 *
 * Options defined here generate a C variable named mono_opt_<flag name> initialized to its default value.
 * The variables are exported using MONO_API.
 * The _READONLY variants generate C const variables so the compiler can optimize away their usage.
 * Option types:
 * BOOL - gboolean
 * INT - int
 * STRING - (malloc-ed) char*
 *
 * Option can be set on the command line using:
 * --[no-]-option (bool)
 * --option=value (int/string)
 * --option value (int/string)
 */

/*
 * This is a template header, the file including this needs to define this macro:
 * DEFINE_OPTION_FULL(flag_type, ctype, c_name, cmd_name, def_value, comment)
 * Optionally, define
 * DEFINE_OPTION_READONLY as well.
 */
#ifndef DEFINE_OPTION_FULL
#error ""
#endif
#ifndef DEFINE_OPTION_READONLY
#define DEFINE_OPTION_READONLY(flag_type, ctype, c_name, cmd_name, def_value, comment) DEFINE_OPTION_FULL(flag_type, ctype, c_name, cmd_name, def_value, comment)
#endif

/* Types of flags */
#define DEFINE_BOOL(name, cmd_name, def_value, comment) DEFINE_OPTION_FULL(MONO_OPTION_BOOL, gboolean, name, cmd_name, def_value, comment)
#define DEFINE_BOOL_READONLY(name, cmd_name, def_value, comment) DEFINE_OPTION_READONLY(MONO_OPTION_BOOL_READONLY, gboolean, name, cmd_name, def_value, comment)
#define DEFINE_INT(name, cmd_name, def_value, comment) DEFINE_OPTION_FULL(MONO_OPTION_INT, int, name, cmd_name, def_value, comment)
#define DEFINE_STRING(name, cmd_name, def_value, comment) DEFINE_OPTION_FULL(MONO_OPTION_STRING, char*, name, cmd_name, def_value, comment)

/*
 * List of runtime flags
 */

/*
DEFINE_BOOL(bool_flag, "bool-flag", FALSE, "Example")
DEFINE_INT(int_flag, "int-flag", 0, "Example")
DEFINE_STRING(string_flag, "string-flag", NULL, "Example")

#ifdef ENABLE_EXAMPLE
DEFINE_BOOL(readonly_flag, "readonly-flag", FALSE, "Example")
#else
DEFINE_BOOL_READONLY(readonly_flag, "readonly-flag", FALSE, "Example")
#endif
*/

DEFINE_BOOL(wasm_exceptions, "wasm-exceptions", FALSE, "Enable codegen for WASM exceptions")
DEFINE_BOOL(wasm_gc_safepoints, "wasm-gc-safepoints", FALSE, "Use GC safepoints on WASM")
DEFINE_BOOL(aot_lazy_assembly_load, "aot-lazy-assembly-load", FALSE, "Load assemblies referenced by AOT images lazily")

#if HOST_BROWSER

// the jiterpreter is not yet thread safe due to the need to synchronize function pointers
//  and wasm modules between threads. before these can be enabled we need to implement all that
#ifdef DISABLE_THREADS
// traces_enabled controls whether the jiterpreter will JIT individual interpreter opcode traces
DEFINE_BOOL(jiterpreter_traces_enabled, "jiterpreter-traces-enabled", FALSE, "JIT interpreter opcode traces into WASM")
// interp_entry_enabled controls whether specialized interp_entry wrappers will be jitted
DEFINE_BOOL(jiterpreter_interp_entry_enabled, "jiterpreter-interp-entry-enabled", TRUE, "JIT specialized WASM interp_entry wrappers")
// jit_call_enabled controls whether do_jit_call will use specialized trampolines for hot call sites
DEFINE_BOOL(jiterpreter_jit_call_enabled, "jiterpreter-jit-call-enabled", TRUE, "JIT specialized WASM do_jit_call trampolines")
#else
// traces_enabled controls whether the jiterpreter will JIT individual interpreter opcode traces
DEFINE_BOOL_READONLY(jiterpreter_traces_enabled, "jiterpreter-traces-enabled", FALSE, "JIT interpreter opcode traces into WASM")
// interp_entry_enabled controls whether specialized interp_entry wrappers will be jitted
DEFINE_BOOL_READONLY(jiterpreter_interp_entry_enabled, "jiterpreter-interp-entry-enabled", FALSE, "JIT specialized WASM interp_entry wrappers")
// jit_call_enabled controls whether do_jit_call will use specialized trampolines for hot call sites
DEFINE_BOOL_READONLY(jiterpreter_jit_call_enabled, "jiterpreter-jit-call-enabled", FALSE, "JIT specialized WASM do_jit_call trampolines")
#endif // DISABLE_THREADS

// enables using WASM try/catch_all instructions where appropriate (currently only do_jit_call),
//  will be automatically turned off if the instructions are not available.
DEFINE_BOOL(jiterpreter_wasm_eh_enabled, "jiterpreter-wasm-eh-enabled", TRUE, "Enable the use of WASM Exception Handling in JITted code")
// if enabled, we will insert trace entry points at backwards branch targets, so that we can
//  JIT loop bodies
DEFINE_BOOL(jiterpreter_backward_branch_entries_enabled, "jiterpreter-backward-branch-entries-enabled", TRUE, "Insert trace entry points at backward branch targets")
// if enabled, after a call instruction terminates a trace, we will attempt to start a new
//  one at the next basic block. this allows jitting loop bodies that start with 'if (x) continue' etc
DEFINE_BOOL(jiterpreter_call_resume_enabled, "jiterpreter-call-resume-enabled", TRUE, "Insert trace entry points after function calls")
// For locations where the jiterpreter heuristic says we will be unable to generate
//  a trace, insert an entry point opcode anyway. This enables collecting accurate
//  stats for options like estimateHeat, but raises overhead.
DEFINE_BOOL(jiterpreter_disable_heuristic, "jiterpreter-disable-heuristic", FALSE, "Always insert trace entry points for more accurate statistics")
// Automatically prints stats at app exit or when jiterpreter_dump_stats is called
DEFINE_BOOL(jiterpreter_stats_enabled, "jiterpreter-stats-enabled", TRUE, "Automatically print jiterpreter statistics")
// Continue counting hits for traces that fail to compile and use it to estimate
//  the relative importance of the opcode that caused them to abort
DEFINE_BOOL(jiterpreter_estimate_heat, "jiterpreter-estimate-heat", FALSE, "Maintain accurate hit count for all trace entry points")
// Count the number of times a trace bails out (branch taken, etc) and for what reason
DEFINE_BOOL(jiterpreter_count_bailouts, "jiterpreter-count-bailouts", FALSE, "Maintain accurate count of all trace bailouts based on cause")
// Dump the wasm blob for all compiled traces
DEFINE_BOOL(jiterpreter_dump_traces, "jiterpreter-dump-traces", FALSE, "Dump the wasm blob for all compiled traces to the console")
// Use runtime imports for pointer constants
// Currently reduces performance significantly :(
DEFINE_BOOL(jiterpreter_use_constants, "jiterpreter-use-constants", FALSE, "Use runtime imports for pointer constants")
// When compiling a jit_call wrapper, bypass sharedvt wrappers if possible by inlining their
//  logic into the compiled wrapper and calling the target AOTed function with native call convention
DEFINE_BOOL(jiterpreter_direct_jit_call, "jiterpreter-direct-jit-calls", TRUE, "Bypass gsharedvt wrappers when compiling JIT call wrappers")
// any trace that doesn't have at least this many meaningful (non-nop) opcodes in it will be rejected
DEFINE_INT(jiterpreter_minimum_trace_length, "jiterpreter-minimum-trace-length", 10, "Reject traces shorter than this number of meaningful opcodes")
// once a trace entry point is inserted, we only actually JIT code for it once it's been hit this many times
DEFINE_INT(jiterpreter_minimum_trace_hit_count, "jiterpreter-minimum-trace-hit-count", 5000, "JIT trace entry points once they are hit this many times")
// After a do_jit_call call site is hit this many times, we will queue it to be jitted
DEFINE_INT(jiterpreter_jit_call_trampoline_hit_count, "jiterpreter-jit-call-hit-count", 1000, "Queue specialized do_jit_call trampoline for JIT after this many hits")
// After a do_jit_call call site is hit this many times without being jitted, we will flush the JIT queue
DEFINE_INT(jiterpreter_jit_call_queue_flush_threshold, "jiterpreter-jit-call-queue-flush-threshold", 5000, "Flush the do_jit_call JIT queue after an unJITted call site has this many hits")
// After a generic interp_entry wrapper is hit this many times, we will queue it to be jitted
DEFINE_INT(jiterpreter_interp_entry_trampoline_hit_count, "jiterpreter-interp-entry-hit-count", 1000, "Queue specialized interp_entry wrapper for JIT after this many hits")
// After a generic interp_entry wrapper is hit this many times without being jitted, we will flush the JIT queue
DEFINE_INT(jiterpreter_interp_entry_queue_flush_threshold, "jiterpreter-interp-entry-queue-flush-threshold", 3000, "Flush the interp_entry JIT queue after an unJITted call site has this many hits")
// In degenerate cases the jiterpreter could end up generating lots of WASM, so shut off jitting once it reaches this limit
// Each wasm byte likely maps to multiple bytes of native code, so it's important for this limit not to be too high
DEFINE_INT(jiterpreter_wasm_bytes_limit, "jiterpreter-wasm-bytes-limit", 6 * 1024 * 1024, "Disable jiterpreter code generation once this many bytes of WASM have been generated")
#endif // HOST_BROWSER

/* Cleanup */
#undef DEFINE_OPTION_FULL
#undef DEFINE_OPTION_READONLY
