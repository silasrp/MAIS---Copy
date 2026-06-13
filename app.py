import json
import logging
import sys
import os
import subprocess
import re
import socket
import ctypes
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Any, Optional
from unittest import result
from unittest import result

from dateutil import parser as date_parser
import requests


# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class CyberArkCredentialManager:
    """Manage credential retrieval from CyberArk vault"""

    def __init__(self, vault_url: str, safe: str, app_id: str, timeout: int = 15):
        """Initialize CyberArk credential manager"""
        self.vault_url = vault_url.rstrip('/')
        self.safe = safe
        self.app_id = app_id
        self.timeout = timeout

    def fetch_password(self, object_name: str) -> str:
        """Fetch password from CyberArk"""
        try:
            # Construct CyberArk REST API URL
            url = f"{self.vault_url}/Accounts?Safe={self.safe}&AppId={self.app_id}&Folder=Root&Object={object_name}&Reason=CONNECT"

            logger.info(f"CyberArk URL: {url}")
            logger.debug(f"Fetching credentials from CyberArk for object: {object_name}")

            # Make HTTPS GET request with timeout
            response = requests.get(url, timeout=self.timeout, verify=True)

            # Check for successful response
            if response.status_code == 404:
                raise ValueError(f"CyberArk object not found: {object_name}")
            elif response.status_code == 401:
                raise PermissionError("CyberArk authentication failed (401). Check app_id credentials.")
            elif response.status_code == 403:
                raise PermissionError("CyberArk access forbidden (403). Check app_id permissions.")
            elif response.status_code != 200:
                raise Exception(f"CyberArk API error: HTTP {response.status_code} - {response.text}")

            # Extract password from response
            password = response.text.strip()

            if not password:
                raise ValueError(f"CyberArk returned empty password for object: {object_name}")

            logger.info(f"Successfully retrieved credentials for object: {object_name}")
            return password

        except requests.exceptions.Timeout:
            raise TimeoutError(f"CyberArk request timed out after {self.timeout} seconds")
        except requests.exceptions.ConnectionError as e:
            raise ConnectionError(f"Failed to connect to CyberArk vault at {self.vault_url}: {str(e)}")
        except requests.exceptions.RequestException as e:
            raise Exception(f"CyberArk request failed: {str(e)}")


class ConfigValidator:
    """Validate configuration file structure and content"""

    @staticmethod
    def validate(config: Dict[str, Any]) -> bool:
        """Validate configuration structure"""
        required_keys = ['machines', 'cyberark']

        for key in required_keys:
            if key not in config:
                raise ValueError(f"Missing required configuration key: {key}")

        # Validate CyberArk config
        cyberark_config = config['cyberark']
        required_cyberark_fields = ['vault_url', 'safe', 'app_id', 'username', 'cyberark_object']
        for field in required_cyberark_fields:
            if field not in cyberark_config:
                raise ValueError(f"CyberArk config missing required field: {field}")

        # Validate vault_url is HTTPS
        vault_url = cyberark_config['vault_url'].lower()
        if not vault_url.startswith('https://'):
            raise ValueError("CyberArk vault_url must use HTTPS (https://)")

        # Validate machines
        if not isinstance(config['machines'], list):
            raise ValueError("'machines' must be a list")

        if len(config['machines']) == 0:
            logger.warning("No machines configured")
            return False

        for idx, machine in enumerate(config['machines']):
            required_fields = ['hostname', 'share_path']
            for field in required_fields:
                if field not in machine:
                    raise ValueError(f"Machine {idx} missing required field: {field}")

        return True


class NetworkShareClient:
    """Handle network share connections via net use"""

    def __init__(self, hostname: str, ip_address: str, share_path: str, username: str, password: str):
        """Initialize network share client"""
        self.hostname = hostname
        self.ip_address = ip_address
        self.share_path = share_path
        self.username = username
        self.password = password
        self.connected = False

    def connect(self) -> bool:
        """Establish network share connection using net use"""
        try:

            # Build UNC path using IP
            unc_path = f"\\\\{self.ip_address}"  # Use IP address instead of hostname
            domain = 'corp'

            pwd_data = json.loads(self.password)

            cmd = [
                'net', 'use', "*",
                unc_path + self.share_path,
                f'/user:{domain}\\{self.username}',
                pwd_data.get("Content"),
                '/persistent:no'
            ]

            result = subprocess.run(cmd, capture_output=True, text=True, check=True, timeout=30)

            if result.returncode == 0:
                logger.info(f"Connected to {unc_path}{self.share_path} successfully")
                self.connected = True
                return True
            else:
                logger.error(f"Net use failed: {result.stdout} {result.stderr}")
                return False

        except Exception as e:
            logger.error(f"Connection error: {str(e)}")
            return False

    def disconnect(self) -> bool:
        """Disconnect from network share"""
        try:
            if not self.connected:
                return True
            
            # Build UNC path using IP
            unc_path = f"\\\\{self.ip_address}"  # Use IP address instead of hostname

            normalized_shared_path = self.share_path.rstrip('\\')
            full_path = unc_path + normalized_shared_path

            result = self.clear_shares_by_path(full_path)

            if result == 0:
                logger.info(f"Disconnected from {self.share_path}")
                self.connected = False
                return True
            else:
                logger.warning(f"Failed to disconnect from {self.share_path}")
                return False

        except Exception as e:
            logger.error(f"Error disconnecting from {self.share_path}: {str(e)}")
            return False

    def clear_shares_by_path(self, target_unc: str):
        # Log information about the live connections
        drive_disconnected = False
        drive_fail_disconnection = False

        # Clean trailing slashes for an exact regex match
        normalized_unc = target_unc.rstrip('\\')
        
        # Query current session mappings
        output = subprocess.run("net use", capture_output=True, text=True, shell=True).stdout
        
        # Regex captures any drive letter mapped to your exact target UNC string
        found_drives = re.findall(r"([A-Z]:)\s+" + re.escape(normalized_unc), output, re.IGNORECASE)
        
        # Explicitly delete by the discovered drive letters
        for drive in found_drives:
            result = subprocess.run(f"net use {drive} /delete /y", shell=True)
            if result.returncode == 0:
                drive_disconnected = True
            else:
                drive_fail_disconnection = True
                logger.error(f"Fail to disconnect from drive {drive} for {normalized_unc}. Result: {result.returncode} {result.stdout} {result.stderr}")
        
        # Logs based on the disconnection results if any drives were found and attempted to disconnect
        if(drive_disconnected and not drive_fail_disconnection):
            logger.info(f"Successfully disconnected drive(s) {', '.join(found_drives)} for {normalized_unc}")
        elif(drive_disconnected and drive_fail_disconnection):
            logger.warning(f"Partially disconnected drive(s) {', '.join(found_drives)} for {normalized_unc}")
            result.returncode = 0  # Consider partial success as overall success
        else:
            logger.error(f"Failed to disconnect from {normalized_unc}")
        
        return result.returncode


    def get_share_path(self) -> str:
        """Return the share path"""
        return self.share_path

class VersionRetriever:
    """Fetch add ins and its versions from remote machines"""

    @staticmethod
    def get_files(client: NetworkShareClient, share_path: str) -> Optional[List[str]]:
        """Fetch add in files from network share"""
        try:
            # Connect to the share
            if not client.connect():
                logger.error(f"Failed to connect to {share_path}")
                return None
            logger.error("THIS IS THE "  + share_path)
            # Find and read file versions
            addInVersions = {}
            try:
                share_dir = Path(share_path)

                # Check if path exists
                if not share_dir.exists():
                    logger.warning(f"Share path does not exist: {share_path}")
                    client.disconnect()
                    return None

                # Find all .dll files
                dll_files = list(share_dir.glob('*.dll'))

                if not dll_files:
                    logger.warning(f"No .dll files found in {share_path}")
                    client.disconnect()
                    return None

                # Read each dll file
                for dll_file in dll_files:
                    try:
                        addInVersions[dll_file.name] = get_dll_version_ctypes(dll_file)
                    except Exception as e:
                        logger.warning(f"Error reading {dll_file.name}: {str(e)}")
                        continue

                logger.info(f"Retrieved {len(dll_files)} .dll files from {share_path}")
                print(addInVersions)
                return addInVersions

            finally:
                # Always disconnect
                client.disconnect()

        except Exception as e:
            logger.error(f"Error retrieving logs from {share_path}: {str(e)}")
            return None

def get_dll_version_ctypes(file_path):
    # Use the Windows version.dll APIs through ctypes so we can read the DLL's
    # embedded VERSIONINFO resource without adding a third-party dependency.
    #
    # High-level flow:
    # 1. Normalize the incoming Path/str into a filesystem path Windows accepts.
    # 2. Ask Windows how large the version-info blob is for this file.
    # 3. Allocate a raw byte buffer of exactly that size.
    # 4. Ask Windows to copy the version-info blob into our buffer.
    # 5. Query the root block ("\\") to get a pointer to VS_FIXEDFILEINFO.
    # 6. Read the packed version fields and unpack them into major.minor.build.revision.
    normalized_path = os.fspath(file_path)
    try:
        # VERSIONINFO data is variable-sized, so Windows first tells us how many
        # bytes are required. A return value of 0 means either the file has no
        # version resource or the API call failed.
        size = ctypes.windll.version.GetFileVersionInfoSizeW(normalized_path, None)
    except Exception as e:
        logger.error(f"Error getting file version info size for {file_path}: {str(e)}")
        return None
    
    if not size:
        return None

    # Allocate a writable native buffer that is owned by this ctypes object.
    # Windows writes the entire VERSIONINFO blob into this block. We do not call
    # free() ourselves; ctypes releases this memory automatically when `res`
    # goes out of scope and is garbage-collected.
    res = ctypes.create_string_buffer(size)

    # Fill `res` with the raw binary VERSIONINFO data for the target file.
    # The second argument is reserved and should be 0.
    ctypes.windll.version.GetFileVersionInfoW(normalized_path, 0, size, res)

    # VerQueryValueW does not copy data out. Instead, it returns a pointer to a
    # view inside `res`, plus the length of that view. Passing "\\" asks for the
    # root block, which is the fixed binary VS_FIXEDFILEINFO structure.
    ptr = ctypes.c_void_p()
    u_len = ctypes.c_uint()
    ctypes.windll.version.VerQueryValueW(res, "\\", ctypes.byref(ptr), ctypes.byref(u_len))

    # Define only the fixed header fields we actually use. dwFileVersionMS and
    # dwFileVersionLS are the two packed 32-bit halves of the full 64-bit file
    # version number.
    class VS_FIXEDFILEINFO(ctypes.Structure):
        _fields_ = [("dwSignature", ctypes.c_uint32),
                    ("dwStrucVersion", ctypes.c_uint32),
                    ("dwFileVersionMS", ctypes.c_uint32),
                    ("dwFileVersionLS", ctypes.c_uint32)]

    # Interpret the address returned by VerQueryValueW as a VS_FIXEDFILEINFO
    # instance. This is safe here because `ptr` points into `res`, and `res`
    # remains alive for the rest of the function.
    info = VS_FIXEDFILEINFO.from_address(ptr.value)

    # Windows stores the version as four 16-bit numbers packed into two 32-bit
    # fields:
    # - dwFileVersionMS: major.high16 | minor.low16
    # - dwFileVersionLS: build.high16 | revision.low16
    ms = info.dwFileVersionMS
    ls = info.dwFileVersionLS

    # Unpack the four version components into the familiar dotted string.
    return f"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}"


class AddonVersionValidator:
    """Main orchestrator for Addon version retrieval"""

    def __init__(self, config_path: str):
        """Initialize with configuration file"""
        self.config = self._load_config(config_path)

        # Initialize CyberArk credential manager
        cyberark_config = self.config['cyberark']
        self.cyberark_manager = CyberArkCredentialManager(
            vault_url=cyberark_config['vault_url'],
            safe=cyberark_config['safe'],
            app_id=cyberark_config['app_id'],
            timeout=cyberark_config.get('timeout', 15)
        )
        self.cyberark_object = cyberark_config['cyberark_object']
        self.cyberark_username = cyberark_config['username']

        # Fetch password from CyberArk
        try:
            self.password = self.cyberark_manager.fetch_password(self.cyberark_object)
            logger.debug(f"CyberArk credentials retrieved.")
        except Exception as e:
            logger.error(f"Failed to fetch credentials from CyberArk: {str(e)}")

    def _load_config(self, config_path: str) -> Dict[str, Any]:
        """Load and validate configuration"""
        try:
            with open(config_path, 'r') as f:
                config = json.load(f)

            if not ConfigValidator.validate(config):
                raise ValueError("Configuration validation failed")

            logger.info(f"Configuration loaded from {config_path}")
            return config
        except json.JSONDecodeError as e:
            logger.error(f"Invalid JSON in configuration file: {str(e)}")
            sys.exit(1)
        except Exception as e:
            logger.error(f"Error loading configuration: {str(e)}")
            sys.exit(1)

    def process_machine(self, machine_config: Dict[str, Any]) -> int:
        """Process a single machine"""
        # Skip disabled machines
        if not machine_config.get('enabled', True):
            logger.info(f"Skipping disabled machine: {machine_config['hostname']}")
            return 0

        hostname = machine_config['hostname']
        logger.info(f"Processing machine: {hostname}")

        # Resolve hostname to IP
        ip_address = socket.gethostbyname(hostname)
        logger.info(f"{hostname} resolved to {ip_address}")

        # Create network share client
        client = NetworkShareClient(
            hostname=hostname,
            ip_address=ip_address,
            share_path=machine_config['share_path'],
            username=self.cyberark_username,
            password=self.password  # Use password fetched from CyberArk
        )

        full_path = "\\\\" + ip_address + machine_config['share_path']
        # Retrieve logs
        files = VersionRetriever.get_files(client, full_path)
        if not files:
            logger.warning(f"No files retrieved from {hostname}")
            return 0

        # Parse logs
        #documents = LogParser.parse_logs(logs, hostname)
        #logger.info(f"Parsed {len(documents)} log entries from {hostname}")

        return 0

    def run(self) -> bool:
        """Main execution method"""
        logger.info("Starting CRIMS Log Retriever")

        # Process each machine
        machines = self.config['machines']
        successful_machines = 0
        failed_machines = 0

        for machine in machines:
            try:
                processed = self.process_machine(machine)
                if processed > 0:
                    successful_machines += 1
                else:
                    failed_machines += 1
            except Exception as e:
                logger.error(f"Error processing machine {machine['hostname']}: {str(e)}")
                failed_machines += 1

        # Log summary
        logger.info(f"Processing complete: {successful_machines} successful, {failed_machines} failed")

        return successful_machines > 0


def main():
    """Entry point"""
    if len(sys.argv) < 2:
        config_path = 'config.json'
        logger.info(f"No config file specified, using default: {config_path}")
    else:
        config_path = sys.argv[1]

    if not os.path.exists(config_path):
        logger.error(f"Configuration file not found: {config_path}")
        sys.exit(1)

    validator = AddonVersionValidator(config_path)
    success = validator.run()

    sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
