
import re

def check_braces(filename):
    with open(filename, 'r') as f:
        lines = f.readlines()

    balance = 0
    stack = []
    
    # Simple state machine to ignore strings and comments
    # This is a naive parser but should be enough for structural braces
    
    for i, line in enumerate(lines):
        line_num = i + 1
        clean_line = ""
        
        # Remove comments
        if "//" in line:
            clean_line = line.split("//")[0]
        else:
            clean_line = line
            
        # Remove strings (naive) - Replace "..." with "" and @"..." with ""
        # Handle simple string usage
        # This regex is approximate
        clean_line = re.sub(r'@"[^"]*"', '', clean_line)
        clean_line = re.sub(r'"(\\.|[^"])*"', '', clean_line)
        
        # Count braces
        open_b = clean_line.count('{')
        close_b = clean_line.count('}')
        
        prev_balance = balance
        balance += (open_b - close_b)
        
        if balance < 0:
            print(f"Error: Negative balance at line {line_num}: {line.strip()}")
            return

        # Heuristic: Check method definitions
        if "private" in line or "public" in line:
             if "class" not in line and "interface" not in line:
                 # Method definition usually happens at balance 1 (inside class)
                 if balance > 1 and "=>" not in line and "=" not in line:
                     # Ignoring lambda or assignment
                     # Check if it looks like a method sig
                     if "(" in line and ")" in line:
                         print(f"Potential nested method at line {line_num} (Balance {balance}): {line.strip()}")

check_braces("/home/mnz/Workspace/architect/my-skeleton/skeleton-api-net/tools/generator/src/Services/SmartProjectGenerator.cs")
