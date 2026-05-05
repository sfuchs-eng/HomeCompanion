#!/usr/bin/env bash

set -u

KNX_GROUP="224.0.23.12"
KNX_PORT="3671"
NIC=""
OUTFILE=""

usage() {
  cat <<'EOF'
Usage:
  ./knx_capture.sh --nic <interface> [--out <file.pcap>] [--group <multicast_ip>] [--port <udp_port>] [--knx]

Options:
  --nic   <interface>    Network interface to capture on (required)
  --out   <file.pcap>    Write packets to a pcap file (openable in Wireshark)
                         Without --out, packets are printed as text to stdout
  --group <multicast_ip> Multicast group address (default: 224.0.23.12)
  --port  <udp_port>     UDP port (default: 3671)
  --knx                  Shortcut for --group 224.0.23.12 --port 3671

Examples:
  ./knx_capture.sh --nic enp3s0 --knx
  ./knx_capture.sh --nic enp3s0 --knx --out capture.pcap
  ./knx_capture.sh --nic enp3s0 --group 224.0.23.12 --port 3671 --out /tmp/knx.pcap

Press Ctrl+C to stop.
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
    --out)
      OUTFILE="${2:-}"
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

if [[ -z "$NIC" ]]; then
  echo "Error: --nic <interface> is required." >&2
  usage
  exit 1
fi

if ! ip link show "$NIC" >/dev/null 2>&1; then
  echo "Error: interface '$NIC' not found." >&2
  exit 1
fi

if ! command -v tcpdump >/dev/null 2>&1; then
  echo "Error: tcpdump is not installed." >&2
  exit 1
fi

FILTER="udp and host ${KNX_GROUP} and port ${KNX_PORT}"

echo "=== KNX multicast capture ==="
echo "Interface : $NIC"
echo "Group     : $KNX_GROUP"
echo "Port      : $KNX_PORT"
echo "Filter    : $FILTER"
if [[ -n "$OUTFILE" ]]; then
  echo "Output    : $OUTFILE (pcap)"
else
  echo "Output    : stdout (text)"
fi
echo "Started   : $(date -Is)"
echo "Press Ctrl+C to stop."
echo "============================================================"

if [[ -n "$OUTFILE" ]]; then
  # Write pcap to file and simultaneously decode + display live text.
  # Pipeline: tcpdump (raw pcap) -> tee (saves file) -> tcpdump -r (decodes to text)
  tcpdump -ni "$NIC" -U -w - "$FILTER" | tee "$OUTFILE" | tcpdump -n -r - -l -tttt 2>/dev/null
else
  tcpdump -ni "$NIC" -l -tttt "$FILTER"
fi
