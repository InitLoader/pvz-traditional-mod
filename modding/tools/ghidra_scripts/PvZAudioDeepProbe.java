// Finds PvZ 1.0.0.1051 SoundManager owners, loader strings and virtual calls.
// @category PvZMod

import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public final class PvZAudioDeepProbe extends GhidraScript {
    private DecompInterface decompiler;

    @Override
    protected void run() throws Exception {
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            printMatchingSymbols();
            printFunctionsUsingScalar(0x4b4L, "GameApp SoundManager field +0x4B4");
            printStringReferences("bass.dll");
            printStringReferences("fmod.dll");
            printStringReferences(".ogg");
            printStringReferences("SOUND_");
        } finally {
            decompiler.dispose();
        }
    }

    private void printMatchingSymbols() {
        println("\n===== AUDIO-LIKE SYMBOLS =====");
        SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
        int count = 0;
        while (symbols.hasNext() && count < 120) {
            Symbol symbol = symbols.next();
            String lower = symbol.getName().toLowerCase();
            if (lower.contains("sound") || lower.contains("audio") ||
                lower.contains("fmod") || lower.contains("bass")) {
                println(symbol.getName(true) + " @ " + symbol.getAddress() +
                    " type=" + symbol.getSymbolType());
                ++count;
            }
        }
    }

    private void printFunctionsUsingScalar(long value, String label) throws Exception {
        Map<Address, Function> functions = new LinkedHashMap<>();
        InstructionIterator instructions = currentProgram.getListing().getInstructions(true);
        while (instructions.hasNext()) {
            Instruction instruction = instructions.next();
            boolean matches = false;
            for (int operand = 0; operand < instruction.getNumOperands() && !matches; ++operand) {
                for (Object object : instruction.getOpObjects(operand)) {
                    if (object instanceof Scalar &&
                        ((Scalar)object).getUnsignedValue() == value) {
                        matches = true;
                        break;
                    }
                }
            }
            if (!matches) continue;
            Function function = getFunctionContaining(instruction.getAddress());
            if (function != null) functions.putIfAbsent(function.getEntryPoint(), function);
        }
        println("\n===== " + label + " functions=" + functions.size() + " =====");
        int count = 0;
        for (Function function : functions.values()) {
            if (count++ >= 50) break;
            decompile(function);
        }
    }

    private void printStringReferences(String needle) throws Exception {
        byte[] bytes = needle.getBytes(StandardCharsets.US_ASCII);
        Address cursor = currentProgram.getMinAddress();
        Set<Address> functions = new LinkedHashSet<>();
        int matches = 0;
        while (cursor != null && matches < 40) {
            Address found = currentProgram.getMemory().findBytes(cursor, bytes, null, true, monitor);
            if (found == null) break;
            println("\nString '" + needle + "' @ " + found);
            for (Reference reference : getReferencesTo(found)) {
                Function function = getFunctionContaining(reference.getFromAddress());
                if (function != null && functions.add(function.getEntryPoint())) {
                    println("  ref " + reference.getFromAddress() + " in " +
                        function.getName() + " @ " + function.getEntryPoint());
                    decompile(function);
                }
            }
            ++matches;
            cursor = found.next();
        }
    }

    private void decompile(Function function) throws Exception {
        println("\n--- " + function.getName() + " @ " + function.getEntryPoint() + " ---");
        DecompileResults result = decompiler.decompileFunction(function, 45, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        } else {
            println("Decompile failed: " + result.getErrorMessage());
        }
    }
}
