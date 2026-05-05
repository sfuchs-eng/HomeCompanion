#!/usr/bin/env bash

set -u

KNX_GROUP="224.0.23.12"
KNX_PORT="3671"
NIC=""

usage() {
  cat <<'EOF'
Usage:
  ./igmp_diagnostic.sh [--nic <interface>] [--group <multicast_ip>] [--port <udp_port>] [--knx]

Examples:
  ./igmp_diagnostic.sh
  ./igmp_diagnostic.sh --knx
  ./igmp_diagnostic.sh --nic enp3s0
  ./igmp_diagnostic.sh --nic eth0 --group 224.0.23.12 --port 3671
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --nic)
      NIC="${2:-}"
      shift 2
      ;;
    --group)
      KNX_GROUP="${2:-}"
      shift 2
      ;;
    --port)
      KNX_PORT="${2:-}"
      shift 2
      ;;
    --knx)
      KNX_GROUP="224.0.23.12"
      KNX_PORT="3671"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -n "$NIC" ]]; then
  if ! ip link show "$NIC" >/dev/null 2>&1; then
    ip link
    echo
    echo "Error: interface '$NIC' not found." >&2
    exit 1
  fi
fi

print_header() {
  printf "\n===== %s =====\n" "$1"
}

run_cmd() {
  local title="$1"
  shift
  print_header "$title"
  echo "+ $*"
  "$@" 2>&1 || true
}

run_shell_cmd() {
  local title="$1"
  local cmd="$2"
  print_header "$title"
  echo "+ $cmd"
  bash -lc "$cmd" 2>&1 || true
}

safe_cat() {
  local title="$1"
  local path="$2"
  print_header "$title"
  if [[ -r "$path" ]]; then
    echo "+ cat $path"
    cat "$path" 2>&1 || true
  else
    echo "Cannot read $path"
  fi
}

safe_sysctl() {
  local key="$1"
  local value
  value="$(sysctl -n "$key" 2>/dev/null || true)"
  if [[ -n "$value" ]]; then
    printf "%-45s = %s\n" "$key" "$value"
  else
    printf "%-45s = <unavailable>\n" "$key"
  fi
}

if [[ -z "$NIC" ]]; then
  NIC="$(ip route get 1.1.1.1 2>/dev/null | awk '/dev/ {for (i=1;i<=NF;i++) if ($i=="dev") {print $(i+1); exit}}')"
fi

print_header "Context"
echo "Timestamp: $(date -Is)"
echo "Hostname : $(hostname 2>/dev/null || true)"
echo "Kernel   : $(uname -srmo 2>/dev/null || true)"
echo "NIC      : ${NIC:-<not-detected>}"
echo "Group    : $KNX_GROUP"
echo "Port     : $KNX_PORT"

run_cmd "Interfaces (brief)" ip -br link
run_cmd "Addresses (brief)" ip -br addr
if [[ -n "$NIC" ]]; then
  run_cmd "Selected NIC details" ip link show "$NIC"
fi

run_cmd "Routes (IPv4)" ip route show
run_shell_cmd "Route lookup for multicast group" "ip route get $KNX_GROUP"

if [[ -n "$NIC" ]]; then
  run_cmd "Multicast memberships (selected NIC)" ip maddr show dev "$NIC"
fi
run_cmd "Multicast memberships (all NICs)" ip maddr show

safe_cat "/proc/net/igmp" "/proc/net/igmp"
safe_cat "/proc/net/igmp6" "/proc/net/igmp6"

print_header "Kernel multicast/IGMP settings"
safe_sysctl net.ipv4.conf.all.mc_forwarding
safe_sysctl net.ipv4.conf.default.mc_forwarding
safe_sysctl net.ipv4.conf.all.rp_filter
safe_sysctl net.ipv4.conf.default.rp_filter
safe_sysctl net.ipv4.igmp_max_memberships
safe_sysctl net.ipv4.igmp_max_msf
if [[ -n "$NIC" ]]; then
  safe_sysctl "net.ipv4.conf.${NIC}.rp_filter"
  safe_sysctl "net.ipv4.conf.${NIC}.mc_forwarding"
fi

print_header "Firewall snapshot"
if command -v nft >/dev/null 2>&1; then
  run_shell_cmd "nftables (sudo -n)" "sudo -n nft list ruleset"
  run_shell_cmd "nftables (without sudo fallback)" "nft list ruleset"
else
  echo "nft not installed"
fi

if command -v iptables >/dev/null 2>&1; then
  run_shell_cmd "iptables -S (sudo -n)" "sudo -n iptables -S"
  run_shell_cmd "iptables -L -n -v (sudo -n)" "sudo -n iptables -L -n -v"
  run_shell_cmd "iptables raw table (sudo -n)" "sudo -n iptables -t raw -L -n -v"
  run_shell_cmd "iptables mangle table (sudo -n)" "sudo -n iptables -t mangle -L -n -v"
else
  echo "iptables not installed"
fi

print_header "Capture commands to run in parallel"
if [[ -n "$NIC" ]]; then
  echo "sudo tcpdump -ni $NIC igmp"
  echo "sudo tcpdump -ni $NIC 'udp and dst host $KNX_GROUP and port $KNX_PORT'"
else
  echo "sudo tcpdump -ni <NIC> igmp"
  echo "sudo tcpdump -ni <NIC> 'udp and dst host $KNX_GROUP and port $KNX_PORT'"
fi

print_header "Done"
echo "Collect this output on both server and laptop, then compare NIC, memberships, route lookup, and packet visibility."
