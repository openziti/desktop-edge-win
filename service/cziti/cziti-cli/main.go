/*
 * Copyright NetFoundry, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

package main

import "C"
import (
	"bufio"
	"errors"
	"fmt"
	"os"
	"strconv"
	"strings"
	"github.com/netfoundry/ziti-tunnel-win/service/cziti"
)

func main() {
	cziti.Start()

	fmt.Println("welcome to cziti-cli testing")
	fmt.Println("----------------------------")
	fmt.Println()

	reader := bufio.NewReader(os.Stdin)
	for {
		fmt.Print("-> ")
		text, _ := reader.ReadString('\n')

		text = strings.TrimSpace(text)
		cmd := strings.Split(text, " ")

		if f, found := commands[cmd[0]]; found {
			err := f(cmd[1:])
			if err != nil {
				_, _ = fmt.Fprintf(os.Stderr, "error: %v", err)
			}
			fmt.Println()
		} else {
			fmt.Printf("command `%s' is not found\n", cmd[0])
		}
	}
}

var commands = make(map[string]func([]string) error)

func init() {
	commands["quit"] = quit
	commands["help"] = help
	commands["list"] = list
	commands["load"] = load
	commands["show-services"] = showServices
}

func showServices(args []string) error {
	if len(args) == 0 {
		return errors.New("select context")
	}

	idx, err := strconv.Atoi(args[0])
	if err != nil {
		return err
	}

	if idx >= len(contexts) {
		return errors.New("invalid reference")
	}

	ctx := contexts[idx]

	fmt.Printf("services available to %s@%s\n", ctx.Name(), ctx.Controller())
	for _, s := range *ctx.Services {
		fmt.Printf("\t%+v\n", s)
	}
	return nil
}

func quit(_ []string) error {
	cziti.Stop()
	os.Exit(0)
	return nil
}

func help(_ []string) error {
	fmt.Println("available commands:")
	for k, _ := range commands {
		fmt.Println("\t", k)
	}
	return nil
}

var contexts []*cziti.CZitiCtx

func load(args []string) error {
	if len(args) == 0 {
		return fmt.Errorf("path to configfile required")
	}

	ctx := cziti.LoadZiti(args[0])
	fmt.Printf("successfully loaded %s@%s\n", ctx.Name(), ctx.Controller())
	contexts = append(contexts, ctx)

	return nil
}

func list(args []string) error {
	fmt.Println("loaded ziti identities:")
	for i, c := range contexts {
		fmt.Printf("\t%d:\t%s@%s\n", i, c.Name(), c.Controller())
	}
	return nil
}
