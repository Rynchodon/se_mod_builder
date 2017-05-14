import os
import sys
from argparse import ArgumentParser, RawDescriptionHelpFormatter
from src import __version__, tasks
from src.lib.config import load_global_config, load_project_config

if hasattr(sys, 'frozen'):
    INSTALL_DIR = os.path.dirname(os.path.realpath(sys.executable))
    ASSET_DIR = sys._MEIPASS
else:
    INSTALL_DIR = ASSET_DIR = os.path.dirname(os.path.realpath(__file__))

DESCRIPTION = """
==== SE Mod Builder ====
Provides tasks to help build and deploy Space Engineers mods.
All commands besides "example-config" should be run from a build event script.

example-config
Run this from your project root to create an example build.yml.

git-version
Updates the VersionInfo for your solution with a revision number from git.
Should be run before compilation so the version is included in your assembly.

build-models
Generates .mwm files from sources.
Should be run before distribution.

distribute-steam
Copy all assets published through Steam to the SE mod folder.
Also removes any outdated files from its distribution paths.

kill-se
Stop and wait for exit of all SE processes, including SE Plugin Loader.
This is useful for SEPL plugins, which are loaded into the SE process.

start-se
Start the SE client. SEPL should be attached with the "plugin" option.
"""


def main():
    """
    Parses provided args and runs specified task
    """
    parser = ArgumentParser(
        description=DESCRIPTION,
        formatter_class=RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        '-v', '--version',
        action='version',
        version=__version__
    )
    parser.add_argument(
        '-d', '--debug',
        action='store_true',
        help='output additional detail, default False',
    )
    parser.add_argument(
        'task',
        choices=[
            'build-models', 'distribute-steam', 'example-config',
            'git-version', 'kill-se', 'start-se'
        ],
        help='the task to run, see above',
        metavar='TASK',
    )
    parser.add_argument(
        '-b', '--build-dir',
        help='path to the built plugin files dir, defaults to "."',
        default='.',
    )
    parser.add_argument(
        '-r', '--root',
        help='path to project root, defaults to "..\..\..\..\.."',
        default='..\..\..\..\..'
    )

    args = parser.parse_args()
    build_dir = os.path.realpath(args.build_dir)
    debug = args.debug
    root_dir = os.path.realpath(args.root)
    task = args.task

    print(' ----- SE Mod Builder {} doing {} ----- '.format(__version__, task))
    global_config = load_global_config(INSTALL_DIR, ASSET_DIR, debug)

    if task == 'example-config':
        tasks.example_config(global_config, os.getcwd())
    else:
        project_config = load_project_config(root_dir, build_dir, debug)
        if task == 'build-models':
            tasks.build_models(global_config, project_config)
        elif task == 'distribute-steam':
            tasks.distribute_steam(global_config, project_config)
        elif task == 'git-version':
            tasks.git_version(global_config, project_config)
        elif task == 'kill-se':
            tasks.kill_se(global_config, project_config)
        elif task == 'start-se':
            tasks.start_se(global_config, project_config)

    # return exit code OK
    print(' ----- SE Mod Builder finished {} ----- '.format(task))
    return 0


if __name__ == '__main__':
    sys.exit(main())
